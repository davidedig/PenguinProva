using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ApocalypseSnow;

public class NewPenguin : DrawableGameComponent
{
    public event Action<ShotStruct>? OnShotReleased;

    private readonly string _tag;
    private int _countBall;
    private readonly Game _gameContext;
    private readonly AnimationManager _animationManager;
    private readonly IPlayerController _controller;

    private Vector2 _position;
    private Vector2 _speed;

    private float _deltaTime;

    private int _ammo;
    public int Ammo { get => _ammo; set => _ammo = value; }

    private float _reloadTime;
    private const int FrameReload = 3;

    private StateStruct _stateStruct;
    private ShotStruct _shotStruct;

    private int _textureFractionWidth;
    private int _textureFractionHeight;
    private int _halfTextureFractionWidth;
    private int _halfTextureFractionHeight;

    private SpriteBatch _spriteBatch = null!;

    private const float MoveHz = 30f;
    private const float MoveDt = 1f / MoveHz;
    private const float MoveSpeed = 200f;
    private float _moveAcc = 0f;

    private PenguinState _penguinState = PenguinState.WalkingSnowball;

    private bool _hasEgg = false;

    public Vector2 Position
    {
        get => _position;
        set => _position = value;
    }

    public NewPenguin(
        Game game,
        Vector2 startPosition,
        Vector2 startSpeed,
        AnimationManager animation,
        IPlayerController controller
    ) : base(game)
    {
        _tag = "penguin";
        _gameContext = game;

        _position = startPosition;
        _speed = startSpeed;

        _ammo = 100;

        _animationManager = animation;
        _controller = controller;

        _stateStruct = new StateStruct();
        _shotStruct = new ShotStruct();
        _countBall = 0;
    }

    private static StateList BuildMaskMovement(StateList raw)
    {
        StateList m = raw & (StateList.Up | StateList.Down | StateList.Left | StateList.Right);

        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0)
            m &= ~(StateList.Left | StateList.Right);

        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0)
            m &= ~(StateList.Up | StateList.Down);

        return m;
    }

    private void SyncCollision()
    {
        int posCollX = (int)_position.X + _halfTextureFractionWidth;
        int posCollY = (int)_position.Y + _halfTextureFractionHeight;

        CollisionManager.Instance.modifyObject(
            _tag,
            posCollX,
            posCollY,
            _textureFractionWidth,
            _textureFractionHeight
        );
    }

    private void Reload(float dt)
    {
        if (!_stateStruct.IsPressed(StateList.Reload)) return;

        _reloadTime += dt;
        if (_reloadTime > FrameReload)
        {
            _ammo++;
            _reloadTime = 0f;
        }
    }

    private void MoveReload()
    {
        if (_stateStruct.JustReleased(StateList.Reload))
            _reloadTime = 0f;
    }

    private void Shot()
    {
        if (!_stateStruct.JustReleased(StateList.Shoot) || _ammo <= 0) return;

        float pressedTime = _controller.GetShotCharge();
        if (pressedTime <= 0f) return;

        // ===== aim offset dx/dy =====
        Vector2 aim = _controller.GetMousePosition();

        // Se è locale, GetMousePosition() è mouse assoluto screen.
        // Convertiamo in offset rispetto al pinguino.
        // (Se è remoto, aim è già offset dx/dy e questa conversione NON deve avvenire.)
        // Trick semplice: il remoto ti manda offset "piccoli" tipicamente (centinaia),
        // mentre lo screen mouse assoluto può essere migliaia. Ma non voglio heuristics.
        // Quindi: locale calcola SEMPRE offset, remoto fornisce offset.
        // Per fare questo senza RTTI, in GameSession userai lo stesso schema:
        // - locale: manda dx/dy in rete (quindi qui calcoliamo dx/dy)
        // - remoto: controller già dx/dy
        //
        // Qui assumiamo che se aim è screen mouse assoluto, allora devi convertirlo.
        // Siccome non posso distinguere affidabilmente senza un flag, faccio la cosa corretta:
        // locale: il controller è LocalPlayerController -> cast safe.
        if (_controller is LocalPlayerController)
        {
            aim = aim - _position; // mouseAbs - pos -> dx/dy
        }

        int dx = (int)aim.X;
        int dy = (int)aim.Y;
        int charge = (int)pressedTime;

        // evento rete: SEMPRE dx/dy + charge
        _shotStruct = new ShotStruct(dx, dy, charge);
        OnShotReleased?.Invoke(_shotStruct);

        // ===== fisica: usa dx/dy coerenti =====
        // Nel tuo modello: differenceX = posX - mouseX = -dx
        // differenceY = posY - mouseY = -dy
        float differenceX = -dx;
        float differenceY = -dy;

        float coX = (differenceX / 100f) * (-1f); // = dx/100
        Vector2 startSpeed = new Vector2(coX, differenceY / 100f) * pressedTime;

        Vector2 finalPosition = PhysicsWrapper.ParabolicMotion(
            150f,
            new Vector2(_position.X + 20, _position.Y),
            new Vector2(startSpeed.X, -startSpeed.Y),
            1.5f
        );

        string tagBall = "PallaNew" + _countBall;
        Ball b = new Ball(_gameContext, _position, startSpeed, finalPosition, tagBall);
        _gameContext.Components.Add(b);

        _ammo--;
        _countBall++;
    }

    private void UpdateFacingFromInput(StateList raw)
    {
        if (_penguinState == PenguinState.Reloading) return;

        bool left = (raw & StateList.Left) != 0;
        bool right = (raw & StateList.Right) != 0;
        bool up = (raw & StateList.Up) != 0;
        bool down = (raw & StateList.Down) != 0;

        if (left) _animationManager.SetRow(1);
        else if (right) _animationManager.SetRow(2);
        else if (down) _animationManager.SetRow(0);
        else if (up) _animationManager.SetRow(3);
    }

    private void ComputeAnim()
    {
        if (_stateStruct.IsPressed(StateList.Reload))
        {
            _penguinState = PenguinState.Reloading;
            return;
        }

        if (_stateStruct.IsPressed(StateList.Shoot) && _ammo > 0)
        {
            _penguinState = PenguinState.Shooting;
            return;
        }

        if (_hasEgg)
        {
            _penguinState = PenguinState.WalkingEgg;
            return;
        }

        _penguinState = (_ammo > 0) ? PenguinState.WalkingSnowball : PenguinState.Walking;
    }

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
        _textureFractionHeight = _animationManager.Texture.Height / 4;

        _halfTextureFractionWidth = _textureFractionWidth / 2;
        _halfTextureFractionHeight = _textureFractionHeight / 2;

        base.LoadContent();
    }

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();
        _animationManager.Draw(_spriteBatch, _position);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    public override void Update(GameTime gameTime)
    {
        _deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _controller.UpdateInput(ref _stateStruct);

        ComputeAnim();

        if (_penguinState == PenguinState.Reloading)
        {
            Reload(_deltaTime);
            MoveReload();

            _moveAcc = 0f;

            _animationManager.Update(_deltaTime, _penguinState, _stateStruct.Current);

            SyncCollision();
            base.Update(gameTime);
            return;
        }

        _moveAcc += _deltaTime;

        StateList raw = _stateStruct.Current;

        while (_moveAcc >= MoveDt)
        {
            _moveAcc -= MoveDt;
            StateList moveMask = BuildMaskMovement(raw);
            _position = PhysicsWrapper.StepFromState(_position, moveMask, MoveSpeed, MoveDt);
        }

        UpdateFacingFromInput(raw);
        MoveReload();

        _animationManager.Update(_deltaTime, _penguinState, raw);

        Shot();

        SyncCollision();
        base.Update(gameTime);
    }
}