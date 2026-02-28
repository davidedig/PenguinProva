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

    private PenguinState _penguinState = PenguinState.WalkingSnowball;
    private bool _hasEgg = false;

    public Vector2 Position { get => _position; set => _position = value; }

    public Vector2 Center => new Vector2(
        _position.X + _halfTextureFractionWidth,
        _position.Y + _halfTextureFractionHeight
    );

    public NewPenguin(Game game,
                      Vector2 startPosition,
                      Vector2 startSpeed,
                      AnimationManager animation,
                      IPlayerController controller) : base(game)
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

    private void SyncCollision()
    {
        CollisionManager.Instance.modifyObject(
            _tag,
            (int)_position.X + _halfTextureFractionWidth,
            (int)_position.Y + _halfTextureFractionHeight,
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
        if (!_stateStruct.JustReleased(StateList.Shoot) || _ammo <= 0)
            return;

        float charge = _controller.ShotCharge;
        if (charge <= 0f) return;

        Vector2 offset = _controller.ShootOffset;

        Vector2 screenDirection = offset;
        if (screenDirection.LengthSquared() > 0) screenDirection.Normalize();
        else screenDirection = new Vector2(1, 0);

        _shotStruct = new ShotStruct(screenDirection.X, screenDirection.Y, charge);
        OnShotReleased?.Invoke(_shotStruct);

        float weaponForceMultiplier = 5f;

        Vector2 startSpeed = new Vector2(screenDirection.X, -screenDirection.Y) * charge * weaponForceMultiplier;
        Vector2 physicsSpeed = new Vector2(startSpeed.X, -startSpeed.Y);

        Vector2 spawnPosition = _position + new Vector2(20, 0);

        Vector2 finalPosition = PhysicsWrapper.ParabolicMotion(
            150f,
            spawnPosition,
            physicsSpeed,
            1.5f
        );

        string tagBall = "PallaNew" + _countBall++;
        Ball b = new Ball(_gameContext, spawnPosition, startSpeed, finalPosition, tagBall);
        _gameContext.Components.Add(b);

        _ammo--;
    }

    private void UpdateFacingFromInput(StateList raw)
    {
        if (_penguinState == PenguinState.Reloading) return;

        if ((raw & StateList.Left) != 0) _animationManager.SetRow(1);
        else if ((raw & StateList.Right) != 0) _animationManager.SetRow(2);
        else if ((raw & StateList.Down) != 0) _animationManager.SetRow(0);
        else if ((raw & StateList.Up) != 0) _animationManager.SetRow(3);
    }

    private void ComputeAnim()
    {
        if (_stateStruct.IsPressed(StateList.Reload)) _penguinState = PenguinState.Reloading;
        else if (_stateStruct.IsPressed(StateList.Shoot) && _ammo > 0) _penguinState = PenguinState.Shooting;
        else if (_hasEgg) _penguinState = PenguinState.WalkingEgg;
        else _penguinState = (_ammo > 0) ? PenguinState.WalkingSnowball : PenguinState.Walking;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _animationManager.Load_Content(GraphicsDevice);

        _textureFractionWidth = _animationManager.Texture.Width / 3;
        _textureFractionHeight = _animationManager.Texture.Height / 4;
        _halfTextureFractionWidth = _textureFractionWidth / 2;
        _halfTextureFractionHeight = _textureFractionHeight / 2;

        CollisionManager.Instance.addObject(_tag, _position.X, _position.Y, _textureFractionWidth, _textureFractionHeight);

        base.LoadContent();
    }

    public override void Update(GameTime gameTime)
    {
        _deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // 1) INPUT (mask, just-pressed/released, shot data)
        _controller.UpdateInput(ref _stateStruct);

        ComputeAnim();

        if (_penguinState == PenguinState.Reloading)
        {
            Reload(_deltaTime);
            MoveReload();

            _animationManager.Update(_deltaTime, _penguinState, _stateStruct.Current);
            SyncCollision();
            base.Update(gameTime);
            return;
        }

        // 2) MOVEMENT (local prediction OR remote snapshot)
        _controller.UpdatePosition(ref _position, _deltaTime, in _stateStruct);

        StateList raw = _stateStruct.Current;

        UpdateFacingFromInput(raw);
        MoveReload();

        _animationManager.Update(_deltaTime, _penguinState, raw);
        Shot();
        SyncCollision();

        base.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();
        _animationManager.Draw(_spriteBatch, _position);
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}