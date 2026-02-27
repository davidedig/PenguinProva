using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;

namespace ApocalypseSnow;

public class Penguin : DrawableGameComponent
{
    private readonly string _tag;
    private int _countBall;
    private readonly Game _gameContext;
    private readonly IAnimation _animationManager;
    private readonly IPlayerController _movementsManager;
    private Vector2 _position;
    private Vector2 _speed;
    private float _pressedTime;
    private float _deltaTime;
    private int _ammo;
    public int Ammo { get => _ammo; set => _ammo = value; }
    private float _reloadTime;
    private StateStruct _stateStruct;
    private ShotStruct _shotStruct;
    private static readonly int FrameReload;
    private int _textureFractionWidth;
    private int _textureFractionHeight;
    private int _halfTextureFractionWidth;
    private int _halfTextureFractionHeight;
    private readonly NetworkManager _networkManager;

    // tick-based movement
    private const float MoveHz = 30f;
    private const float MoveDt = 1f / MoveHz;
    private const float MoveSpeed = 200f;
    private float _moveAcc = 0f;

    // seq tick
    private uint _seq = 0;

    // ==========================
    // Prediction + Reconciliation
    // ==========================
    private const int BufSize = 1024; // power of 2
    private readonly StateList[] _inputBuf = new StateList[BufSize]; // maskNet per tick
    private readonly uint[] _seqBuf = new uint[BufSize];
    private readonly Vector2[] _predPosBuf = new Vector2[BufSize];

    private readonly object _authLock = new object();
    private bool _authPending;
    private uint _authAck;
    private Vector2 _authPos;

    private uint _lastAppliedAck;

    private const float PosEps = 0.75f; // soglia correzione

    // ===== debug throttling (1Hz) =====
    private long _lastReconLogMs = 0;
    private SpriteBatch _spriteBatch;

    private static long NowMs() => Environment.TickCount64;
    private void ReconLog1Hz(string msg)
    {
        long now = NowMs();
        if (now - _lastReconLogMs >= 1000)
        {
            _lastReconLogMs = now;
            Debug.WriteLine(msg);
        }
    }

    static Penguin()
    {
        FrameReload = 3;
    }

    public Penguin(
        Game game,
        Vector2 startPosition,
        Vector2 startSpeed,
        IAnimation animation,
        IPlayerController movements,
        NetworkManager networkManager
    ) : base(game)
    {
        _tag = "penguin";
        _gameContext = game;
        _position = startPosition;
        _speed = startSpeed;
        _ammo = 100;
        _animationManager = animation;
        _movementsManager = movements;
        _stateStruct = new StateStruct();
        _shotStruct = new ShotStruct();
        _countBall = 0;
        _networkManager = networkManager;

        // RICEZIONE AUTH: salva snapshot (thread rete), applicazione in Update (main thread)
        _networkManager.OnAuthState += (ack, x, y) =>
        {
            lock (_authLock)
            {
                if (!_authPending || ack > _authAck)
                {
                    _authAck = ack;
                    _authPos = new Vector2(x, y);
                    _authPending = true;
                }
            }
        };
    }

    // ===== gameplay invariato =====

    private void Reload(float gameTime)
    {
        if (!_stateStruct.IsPressed(StateList.Reload)) return;
        _reloadTime += gameTime;

        if (_reloadTime > FrameReload)
        {
            _ammo++;
            _reloadTime = 0f;
        }
    }

    private void ChargeShot(ref float pressedTime, float deltaTime)
    {
        if (!_stateStruct.IsPressed(StateList.Shoot) || _ammo <= 0) return;

        pressedTime += deltaTime * 166.67f;
        if (pressedTime > 500f) pressedTime = 500f;
    }

    private void Shot(float pressedTime)
    {
        if (!_stateStruct.JustReleased(StateList.Shoot) || _ammo <= 0) return;

        Vector2 mousePosition = _movementsManager.GetMousePosition();
        _shotStruct.mouseX = (int)mousePosition.X;
        _shotStruct.mouseY = (int)mousePosition.Y;

        float differenceX = _position.X - mousePosition.X;
        float differenceY = _position.Y - mousePosition.Y;

        float coX = (differenceX / 100) * (-1);
        Vector2 startSpeed = new Vector2(coX, differenceY / 100) * pressedTime;

        Vector2 finalPosition = FinalPoint(startSpeed, _position);

        string tagBall = "Palla" + _countBall;
        Ball b = new Ball(_gameContext, _position, startSpeed, finalPosition, tagBall);
        _gameContext.Components.Add(b);
        _networkManager.SendShot(_shotStruct);

        _pressedTime = 0;
        _ammo--;
        _countBall++;
    }

    private Vector2 FinalPoint(Vector2 startSpeed, Vector2 startPosition)
    {
        return PhysicsWrapper.ParabolicMotion(
            150f,
            new Vector2(startPosition.X + 20, startPosition.Y),
            new Vector2(startSpeed.X, -startSpeed.Y),
            1.5f
        );
    }

    private void MoveReload()
    {
        if (_stateStruct.JustReleased(StateList.Reload))
        {
            _reloadTime = 0f;
        }
    }

    // ===== MonoGame boilerplate =====

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _animationManager.Load_Content(GraphicsDevice);

        CollisionManager.Instance.addObject(
            _tag,
            _position.X,
            _position.Y,
            _animationManager.Texture.Width,
            _animationManager.Texture.Height
        );

        _textureFractionWidth = _animationManager.Texture.Width / 3;
        _textureFractionHeight = _animationManager.Texture.Height / 3;
        _halfTextureFractionWidth = _textureFractionWidth / 2;
        _halfTextureFractionHeight = _textureFractionHeight / 2;
    }

   /* public void Draw(SpriteBatch spriteBatch)
    {
        _animationManager.Draw(
            spriteBatch,
            ref _position,
            _ammo,
            _stateStruct.IsPressed(StateList.Reload),
            _stateStruct.IsPressed(StateList.Shoot)
        );
    } */

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();

        // qui dentro metti ESATTAMENTE la tua roba
        _animationManager.Draw(
            _spriteBatch,
            ref _position,
            _ammo,
            _stateStruct.IsPressed(StateList.Reload),
            _stateStruct.IsPressed(StateList.Shoot)
        );

        _spriteBatch.End();
    }

    // ===== prediction/reconcile helpers =====

    private static int Idx(uint s) => (int)(s & (BufSize - 1));

    private void SavePrediction(uint seq, StateList maskNet, Vector2 predictedPos)
    {
        int i = Idx(seq);
        _seqBuf[i] = seq;
        _inputBuf[i] = maskNet;
        _predPosBuf[i] = predictedPos;
    }

    private bool TryGetPredictedPos(uint seq, out Vector2 pos)
    {
        int i = Idx(seq);
        if (_seqBuf[i] == seq)
        {
            pos = _predPosBuf[i];
            return true;
        }
        pos = default;
        return false;
    }

    private bool TryGetInput(uint seq, out StateList maskNet)
    {
        int i = Idx(seq);
        if (_seqBuf[i] == seq)
        {
            maskNet = _inputBuf[i];
            return true;
        }
        maskNet = default;
        return false;
    }

    private void ApplyReconciliationIfAny()
    {
        uint ack;
        Vector2 authPos;

        lock (_authLock)
        {
            if (!_authPending) return;
            ack = _authAck;
            authPos = _authPos;
            _authPending = false;
        }

        if (ack <= _lastAppliedAck) return;
        if (ack > _seq) return;

        // serve predizione al tick ack
        if (!TryGetPredictedPos(ack, out var predAtAck))
        {
            _position = authPos;       // hard snap
            _lastAppliedAck = ack;

            ReconLog1Hz($"[RECON] HARD SNAP ack={ack} (no history) localSeq={_seq}");
            return;
        }

        float err = Vector2.Distance(predAtAck, authPos);
        if (err <= PosEps)
        {
            _lastAppliedAck = ack;
            return;
        }

        ReconLog1Hz($"[RECON] APPLY ack={ack} localSeq={_seq} err={err:F2}");

        // REWIND
        Vector2 replayPos = authPos;

        // REPLAY da ack+1 a _seq
        for (uint s = ack + 1; s <= _seq; s++)
        {
            if (!TryGetInput(s, out var maskNet))
                break;

            // maskNet contiene anche Shoot, ma StepFromState lo ignora (usa solo UDLR+Reload)
            replayPos = PhysicsWrapper.StepFromState(replayPos, maskNet, MoveSpeed, MoveDt);

            // aggiorna cache predizioni corrette
            _predPosBuf[Idx(s)] = replayPos;
        }

        _position = replayPos;
        _lastAppliedAck = ack;
    }

    private static StateList BuildMaskNet(StateList raw)
    {
        // SOLO input veri (NO Moving)
        StateList m = raw & (StateList.Up | StateList.Down | StateList.Left | StateList.Right | StateList.Reload | StateList.Shoot);

        // cancella opposti LR
        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0)
            m &= ~(StateList.Left | StateList.Right);

        // cancella opposti UD
        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0)
            m &= ~(StateList.Up | StateList.Down);

        return m;
    }

    public override void Update(GameTime gameTime)
    {
        _deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // aggiorna input locale
        _movementsManager.UpdateInput(ref _stateStruct);

        // tick a 30Hz
        _moveAcc += _deltaTime;
        while (_moveAcc >= MoveDt)
        {
            _moveAcc -= MoveDt;
            _seq++;

            // maschera "raw" dal tuo sistema input
            StateList raw = _stateStruct.Current;

            // maschera pulita per rete/simulazione
            StateList maskNet = BuildMaskNet(raw);

            // PREDICTION locale (DLL)
            _position = PhysicsWrapper.StepFromState(_position, maskNet, MoveSpeed, MoveDt);

            // salva per reconcile
            SavePrediction(_seq, maskNet, _position);

            // manda al server (maskNet, seq)
            _networkManager.SendStateTick(maskNet, _seq);

            // reconcile (senza smoothing)
            ApplyReconciliationIfAny();
        }

        // orientazione sprite (usa raw, non maskNet, così non ti cambia la logica animazione)
        bool left = _stateStruct.IsPressed(StateList.Left);
        bool right = _stateStruct.IsPressed(StateList.Right);
        bool up = _stateStruct.IsPressed(StateList.Up);
        bool down = _stateStruct.IsPressed(StateList.Down);

        if (left)
            _animationManager.MoveRect(1 * _animationManager.SourceRect.Height);
        else if (right)
            _animationManager.MoveRect(2 * _animationManager.SourceRect.Height);
        else if (up)
            _animationManager.MoveRect(3 * _animationManager.SourceRect.Height);
        else if (down)
            _animationManager.MoveRect(0 * _animationManager.SourceRect.Height);

        MoveReload();

        _animationManager.Update(
            _deltaTime,
            _stateStruct.IsPressed(StateList.Moving),
            _stateStruct.IsPressed(StateList.Reload)
        );

        // gameplay invariato
        Reload(_deltaTime);
        ChargeShot(ref _pressedTime, _deltaTime);
        Shot(_pressedTime);

        // collision invariata
        int posCollX = (int)_position.X + _halfTextureFractionWidth;
        int posCollY = (int)_position.Y + _halfTextureFractionHeight;
        CollisionManager.Instance.modifyObject(
            _tag,
            posCollX,
            posCollY,
            _textureFractionWidth,
            _textureFractionHeight
        );

        base.Update(gameTime);
    }
}