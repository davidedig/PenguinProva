using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;

namespace ApocalypseSnow;

public record struct JoinSnapshot(uint PlayerId, Vector2 SpawnPos);
public record struct AuthSnapshot(uint Ack, Vector2 Position);
public record struct RemoteSnapshot(Vector2 Position, StateList Mask);

public sealed class GameSession : GameComponent, IDisposable
{
    public event Action<IGameComponent>? OnEntitySpawned;
    public event Action<IGameComponent>? OnEntityDestroyed;

    private const string ServerAddr = "4.tcp.eu.ngrok.io";
    private const int ServerPort = 10083;

    private const float NetHz = 30f;
    private const float NetDt = 1f / NetHz;
    private const float MoveSpeed = 200f;

    private readonly NetworkManager _networkManager;
    private readonly CollisionManager _collisionManager;
    private readonly AnimationManager _localAnimationManager;
    private readonly AnimationManager _remoteAnimationManager;

    private readonly LocalPlayerController _localController;
    private readonly RemotePlayerController _remoteController;

    private readonly Reconciler _reconciler = new();

    private NewPenguin? _localPenguin;
    private NewPenguin? _remotePenguin;
    private Obstacle? _obstacle;

    private bool _joinSent;
    private bool _spawned;
    private uint _playerId;

    private long _frameId;
    private StateStruct _frameInput = new();

    // hard 30Hz scheduler for sending input
    private bool _netInit;
    private double _nextNetTickSec;

    private readonly ConcurrentQueue<JoinSnapshot> _joinQueue = new();
    private readonly ConcurrentQueue<AuthSnapshot> _authQueue = new();
    private readonly ConcurrentQueue<RemoteSnapshot> _remoteStateQueue = new();
    private readonly ConcurrentQueue<ShotStruct> _shotQueue = new();

    public int LocalPlayerAmmo => _localPenguin?.Ammo ?? 0;
    public bool IsConnected => _networkManager.IsConnected;
    public uint PlayerId => _playerId;

    public GameSession(Game gameContext) : base(gameContext)
    {
        UpdateOrder = -1000;

        _networkManager = new NetworkManager(ServerAddr, ServerPort);
        _collisionManager = new CollisionManager(gameContext);

        _localAnimationManager = new AnimationManager("blue");
        _remoteAnimationManager = new AnimationManager("red");

        _localController = new LocalPlayerController();
        _remoteController = new RemotePlayerController();

        _networkManager.OnJoinAck += (pid, x, y) => _joinQueue.Enqueue(new JoinSnapshot(pid, new Vector2(x, y)));
        _networkManager.OnAuthState += (ack, x, y) => _authQueue.Enqueue(new AuthSnapshot(ack, new Vector2(x, y)));
        _networkManager.OnRemoteState += (x, y, mask) => _remoteStateQueue.Enqueue(new RemoteSnapshot(new Vector2(x, y), mask));
        _networkManager.OnRemoteShot += (dx, dy, charge) => _shotQueue.Enqueue(new ShotStruct(dx, dy, charge));
    }

    public void Start()
    {
        _networkManager.StartAutoReconnect(TimeSpan.FromSeconds(5));
        OnEntitySpawned?.Invoke(_collisionManager);
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        double nowSec = gameTime.TotalGameTime.TotalSeconds;

        ProcessServerMessages();
        UpdateLocalInput(dt);
        SendInputToServer(nowSec);

        base.Update(gameTime);
    }

    private void ProcessServerMessages()
    {
        GetAll(_joinQueue, join =>
        {
            if (!_spawned) HandleSpawn(join);
        });

        // reconcile only on auth
        GetLatest(_authQueue, auth =>
        {
            _reconciler.OnServerAuth(auth.Ack, auth.Position);

            if (_spawned && _localPenguin != null)
            {
                Vector2 p = _localPenguin.Position;
                _reconciler.Apply(ref p, MoveSpeed, NetDt);
                _localPenguin.Position = p;
            }
        });

        GetLatest(_remoteStateQueue, HandleRemoteUpdate);

        GetAll(_shotQueue, shot =>
        {
            _remoteController.ApplyRemoteShot(shot.dirX, shot.dirY, shot.charge);
        });
    }

    private void UpdateLocalInput(float dt)
    {
        _frameId++;

        // aim offset belongs to controller
        if (_localPenguin != null)
            _localController.OwnerCenter = _localPenguin.Center;

        _localController.BeginFrame(_frameId, dt, Game.IsActive);
        _localController.UpdateInput(ref _frameInput);

        if (_networkManager.IsConnected && !_joinSent)
        {
            _networkManager.SendJoin();
            _joinSent = true;
        }
    }

    private void SendInputToServer(double nowSec)
    {
        if (!_spawned) return;

        if (!_netInit)
        {
            _netInit = true;
            _nextNetTickSec = nowSec;
        }

        int maxTicksThisFrame = 5;
        while (maxTicksThisFrame-- > 0 && nowSec >= _nextNetTickSec)
        {
            _nextNetTickSec += NetDt;

            StateList moveInput = GetMovementOnly(_frameInput.Current);
            StateList actionInput = GetActionsOnly(_frameInput.Current);

            uint sequence = _reconciler.NextSeq();
            _reconciler.Record(sequence, moveInput);

            _networkManager.SendStateTick(moveInput | actionInput, sequence);
        }
    }

    private void HandleSpawn(JoinSnapshot data)
    {
        _playerId = data.PlayerId;

        _localPenguin = new NewPenguin(Game, data.SpawnPos, Vector2.Zero, _localAnimationManager, _localController);
        _localPenguin.OnShotReleased += shot => _networkManager.SendShot(shot);

        _obstacle = new Obstacle(Game, new Vector2(100, 100), 1, 1);

        OnEntitySpawned?.Invoke(_obstacle);
        OnEntitySpawned?.Invoke(_localPenguin);

        _reconciler.Reset();
        _netInit = false;
        _spawned = true;
    }

    private void HandleRemoteUpdate(RemoteSnapshot data)
    {
        StateList mask = FilterInput(
            data.Mask,
            StateList.Up | StateList.Down | StateList.Left | StateList.Right |
            StateList.Reload | StateList.Shoot
        );

        // remote is server-authoritative: snapshot position + mask for anim/shoot
        _remoteController.ApplyRemoteState(mask);
        _remoteController.ApplyRemotePosition(data.Position);

        if (_remotePenguin == null)
        {
            _remotePenguin = new NewPenguin(Game, data.Position, Vector2.Zero, _remoteAnimationManager, _remoteController);
            OnEntitySpawned?.Invoke(_remotePenguin);
        }
    }

    private void GetAll<T>(ConcurrentQueue<T> queue, Action<T> action)
    {
        while (queue.TryDequeue(out T? item)) action(item);
    }

    private void GetLatest<T>(ConcurrentQueue<T> queue, Action<T> action)
    {
        T? latest = default;
        bool hasData = false;

        while (queue.TryDequeue(out T? item))
        {
            latest = item;
            hasData = true;
        }

        if (hasData && latest != null) action(latest);
    }

    private StateList GetMovementOnly(StateList raw) =>
        FilterInput(raw, StateList.Up | StateList.Down | StateList.Left | StateList.Right);

    private StateList GetActionsOnly(StateList raw) =>
        FilterInput(raw, StateList.Reload | StateList.Shoot);

    private static StateList FilterInput(StateList raw, StateList allowed)
    {
        StateList m = raw & allowed;

        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0) m &= ~(StateList.Left | StateList.Right);
        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0) m &= ~(StateList.Up | StateList.Down);

        return m;
    }

    public void Stop()
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