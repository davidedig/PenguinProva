using System;
using Microsoft.Xna.Framework;

namespace ApocalypseSnow;

public sealed class GameSession : GameComponent, IDisposable
{
    public event Action<IGameComponent>? OnEntitySpawned;
    public event Action<IGameComponent>? OnEntityDestroyed;

    private const string ServerAddr = "127.0.0.1";
    private const int ServerPort = 8080;

    private const float NetHz = 30f;
    private const float NetDt = 1f / NetHz;

    private const float MoveSpeed = 200f;

    private readonly NetworkManager _networkManager;
    private readonly CollisionManager _collisionManager;
    private readonly AnimationManager _animationManager;
    private readonly AnimationManager _remoteAnimationManager;

    private readonly LocalPlayerController _localController;
    private readonly RemotePlayerController _remoteController;

    private readonly Reconciler _reconciler = new Reconciler();

    private NewPenguin? _localPenguin;
    private NewPenguin? _remotePenguin;
    private Obstacle? _obstacle;

    private bool _joinSent;
    private bool _spawned;
    private uint _playerId;

    private float _netAcc;
    private long _frameId;

    private StateStruct _frameInput;

    public int LocalPlayerAmmo => _localPenguin?.Ammo ?? 0;
    public bool IsConnected => _networkManager.IsConnected;
    public uint PlayerId => _playerId;

    // ===== thread-safe buffers =====
    private readonly object _joinLock = new();
    private bool _pendingJoin;
    private uint _pendingPlayerId;
    private Vector2 _pendingSpawn;

    private readonly object _authLock = new();
    private bool _pendingAuth;
    private uint _pendingAck;
    private Vector2 _pendingAuthPos;

    private readonly object _remoteLock = new();
    private bool _pendingRemote;
    private Vector2 _pendingRemotePos;
    private StateList _pendingRemoteMask;

    private readonly object _shotLock = new();
    private bool _pendingRemoteShot;
    private int _pendingShotA;       // dx
    private int _pendingShotB;       // dy
    private float _pendingShotCharge;

    public GameSession(Game gameContext) : base(gameContext)
    {
        _networkManager = new NetworkManager(ServerAddr, ServerPort);
        _collisionManager = new CollisionManager(gameContext);

        _animationManager = new AnimationManager("blue");
        _remoteAnimationManager = new AnimationManager("red");

        _localController = new LocalPlayerController();
        _remoteController = new RemotePlayerController();

        _frameInput = new StateStruct();

        _networkManager.OnJoinAck += (pid, x, y) =>
        {
            lock (_joinLock)
            {
                _pendingJoin = true;
                _pendingPlayerId = pid;
                _pendingSpawn = new Vector2(x, y);
            }
        };

        _networkManager.OnAuthState += (ack, x, y) =>
        {
            lock (_authLock)
            {
                _pendingAuth = true;
                _pendingAck = ack;
                _pendingAuthPos = new Vector2(x, y);
            }
        };

        _networkManager.OnRemoteState += (x, y, mask) =>
        {
            lock (_remoteLock)
            {
                _pendingRemote = true;
                _pendingRemotePos = new Vector2(x, y);
                _pendingRemoteMask = mask;
            }
        };

        // RemoteShot: (a,b,charge) dove a,b sono dx/dy (offset aim) nel nostro schema
        _networkManager.OnRemoteShot += (a, b, charge) =>
        {
            lock (_shotLock)
            {
                _pendingRemoteShot = true;
                _pendingShotA = a;
                _pendingShotB = b;
                _pendingShotCharge = charge;
            }
        };
    }

    public void Start()
    {
        _networkManager.StartAutoReconnect(TimeSpan.FromSeconds(5));
        OnEntitySpawned?.Invoke(_collisionManager);
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // ===== 0) snapshot input (UNA volta) =====
        _frameId++;
        _localController.BeginFrame(_frameId, dt, Game.IsActive);
        _localController.UpdateInput(ref _frameInput);

        // ===== 1) handshake join =====
        if (_networkManager.IsConnected && !_joinSent)
        {
            _networkManager.SendJoin();
            _joinSent = true;
        }

        // ===== 2) spawn locale su JoinAck =====
        TryConsumeJoinAckAndSpawn();

        // ===== 3) remoto: state/move/anim =====
        TryConsumeRemoteAndSpawnOrMove();

        // ===== 4) remoto: evento shot (dx/dy + charge) =====
        TryConsumeRemoteShotAndFeedController();

        if (_spawned && _localPenguin != null)
        {
            // ===== 5) auth -> reconciler =====
            TryConsumeAuthAndFeedReconciler();

            // ===== 6) tick rete 30Hz =====
            _netAcc += dt;
            while (_netAcc >= NetDt)
            {
                _netAcc -= NetDt;

                StateList raw = _frameInput.Current;

                StateList maskNet = BuildMaskNet(raw);
                StateList moveMask = BuildMaskMovement(raw);

                uint seq = _reconciler.NextSeq();
                _reconciler.Record(seq, moveMask);

                _networkManager.SendStateTick(maskNet, seq);
            }

            // ===== 7) apply reconcile sulla posizione locale =====
            Vector2 p = _localPenguin.Position;
            _reconciler.Apply(ref p, MoveSpeed, NetDt);
            _localPenguin.Position = p;
        }

        base.Update(gameTime);
    }

    private void TryConsumeJoinAckAndSpawn()
    {
        bool doSpawn;
        uint pid;
        Vector2 spawn;

        lock (_joinLock)
        {
            doSpawn = _pendingJoin;
            pid = _pendingPlayerId;
            spawn = _pendingSpawn;
            _pendingJoin = false;
        }

        if (!doSpawn || _spawned) return;

        _playerId = pid;

        _localPenguin = new NewPenguin(
            Game,
            spawn,
            Vector2.Zero,
            _animationManager,
            _localController
        );

        // locale inoltra lo shot al server:
        // shot è già (dx,dy,charge) perché NewPenguin costruisce l'offset prima di invocare l'evento
        _localPenguin.OnShotReleased += shot => _networkManager.SendShot(shot);

        _obstacle = new Obstacle(Game, new Vector2(100, 100), 1, 1);

        OnEntitySpawned?.Invoke(_obstacle);
        OnEntitySpawned?.Invoke(_localPenguin);

        _netAcc = 0f;
        _reconciler.Reset();

        _spawned = true;
    }

    private void TryConsumeAuthAndFeedReconciler()
    {
        bool has;
        uint ack;
        Vector2 pos;

        lock (_authLock)
        {
            has = _pendingAuth;
            ack = _pendingAck;
            pos = _pendingAuthPos;
            _pendingAuth = false;
        }

        if (!has) return;

        _reconciler.OnServerAuth(ack, pos);
    }

    private void TryConsumeRemoteAndSpawnOrMove()
    {
        bool has;
        Vector2 pos;
        StateList mask;

        lock (_remoteLock)
        {
            has = _pendingRemote;
            pos = _pendingRemotePos;
            mask = _pendingRemoteMask;
            _pendingRemote = false;
        }

        if (!has || !_spawned) return;

        // remoto deve ricevere anche Reload + Shoot
        mask = BuildMask(mask,
            StateList.Up | StateList.Down | StateList.Left | StateList.Right |
            StateList.Reload | StateList.Shoot
        );

        _remoteController.ApplyRemoteState(mask);

        if (_remotePenguin == null)
        {
            _remotePenguin = new NewPenguin(
                Game,
                pos,
                Vector2.Zero,
                _remoteAnimationManager,
                _remoteController
            );

            OnEntitySpawned?.Invoke(_remotePenguin);
            return;
        }

        _remotePenguin.Position = pos;
    }

    private void TryConsumeRemoteShotAndFeedController()
    {
        bool has;
        int a, b;
        float charge;

        lock (_shotLock)
        {
            has = _pendingRemoteShot;
            a = _pendingShotA;
            b = _pendingShotB;
            charge = _pendingShotCharge;
            _pendingRemoteShot = false;
        }

        if (!has) return;

        // a,b = dx,dy offset (coerente con NewPenguin + server)
        _remoteController.ApplyRemoteShot(a, b, charge);
    }

    private static StateList BuildMaskNet(StateList raw)
    {
        StateList m = raw & (
            StateList.Up | StateList.Down | StateList.Left | StateList.Right |
            StateList.Reload | StateList.Shoot
        );

        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0)
            m &= ~(StateList.Left | StateList.Right);

        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0)
            m &= ~(StateList.Up | StateList.Down);

        return m;
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

    private static StateList BuildMask(StateList raw, StateList allowed)
    {
        StateList m = raw & allowed;

        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0)
            m &= ~(StateList.Left | StateList.Right);

        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0)
            m &= ~(StateList.Up | StateList.Down);

        return m;
    }

    private void Stop()
    {
        if (_localPenguin != null) OnEntityDestroyed?.Invoke(_localPenguin);
        if (_remotePenguin != null) OnEntityDestroyed?.Invoke(_remotePenguin);
        if (_obstacle != null) OnEntityDestroyed?.Invoke(_obstacle);

        OnEntityDestroyed?.Invoke(_collisionManager);
    }

    public new void Dispose()
    {
        Stop();
        _networkManager.Dispose();
        base.Dispose();
    }
}