using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace ApocalypseSnow;

public sealed class LocalPlayerController : IPlayerController
{
    private long _lastFrameId = -1;

    // stato input (Old/Current)
    private StateStruct _state;

    // mouse/shot
    private Vector2 _mousePos;
    private float _shotCharge;

    // il pinguino locale aggiorna questo valore ogni frame
    public Vector2 OwnerCenter { get; set; }

    public Vector2 ShootOffset => _mousePos - OwnerCenter;
    public float ShotCharge => _shotCharge;

    // movement (fixed-step)
    private const float MoveHz = 30f;
    private const float MoveDt = 1f / MoveHz;
    private const float MoveSpeed = 200f;
    private float _moveAcc = 0f;

    // --- INPUT ---

    public void BeginFrame(long frameId, float dt, bool isActive)
    {
        if (_lastFrameId == frameId) return;
        _lastFrameId = frameId;

        _state.Old = _state.Current;
        _state.Current = StateList.None;

        if (!isActive)
        {
            _shotCharge = 0f;
            return;
        }

        var k = Keyboard.GetState();
        if (k.IsKeyDown(Keys.W)) _state.Current |= StateList.Up;
        if (k.IsKeyDown(Keys.S)) _state.Current |= StateList.Down;
        if (k.IsKeyDown(Keys.A)) _state.Current |= StateList.Left;
        if (k.IsKeyDown(Keys.D)) _state.Current |= StateList.Right;
        if (k.IsKeyDown(Keys.R)) _state.Current |= StateList.Reload;

        var m = Mouse.GetState();
        _mousePos = new Vector2(m.X, m.Y);

        bool wasShooting = (_state.Old & StateList.Shoot) != 0;

        if (m.LeftButton == ButtonState.Pressed)
        {
            if (!wasShooting) _shotCharge = 0f;

            _state.Current |= StateList.Shoot;
            _shotCharge += dt * 166.67f;
            if (_shotCharge > 500f) _shotCharge = 500f;
        }

        // clean opposite directions
        if ((_state.Current & StateList.Left) != 0 && (_state.Current & StateList.Right) != 0)
            _state.Current &= ~(StateList.Left | StateList.Right);

        if ((_state.Current & StateList.Up) != 0 && (_state.Current & StateList.Down) != 0)
            _state.Current &= ~(StateList.Up | StateList.Down);

        // optional "moving" bit
        if ((_state.Current & (StateList.Up | StateList.Down | StateList.Left | StateList.Right)) != 0)
            _state.Current |= StateList.Moving;
    }

    public void UpdateInput(ref StateStruct state)
    {
        state.Old = _state.Old;
        state.Current = _state.Current;
    }

    // --- MOVEMENT (PREDICTION) ---

    private static StateList BuildMoveMask(StateList raw)
    {
        StateList m = raw & (StateList.Up | StateList.Down | StateList.Left | StateList.Right);
        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0) m &= ~(StateList.Left | StateList.Right);
        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0) m &= ~(StateList.Up | StateList.Down);
        return m;
    }

    public void UpdatePosition(ref Vector2 position, float dt, in StateStruct state)
    {
        // movimento: se stai reloadando, niente movement
        if ((state.Current & StateList.Reload) != 0)
        {
            _moveAcc = 0f;
            return;
        }

        _moveAcc += dt;
        StateList raw = state.Current;

        while (_moveAcc >= MoveDt)
        {
            _moveAcc -= MoveDt;
            StateList moveMask = BuildMoveMask(raw);
            position = PhysicsWrapper.StepFromState(position, moveMask, MoveSpeed, MoveDt);
        }
    }
}