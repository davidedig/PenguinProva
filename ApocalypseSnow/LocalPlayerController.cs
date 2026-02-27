using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace ApocalypseSnow;

public sealed class LocalPlayerController : IPlayerController
{
    private long _frameIdCached = -1;

    private StateStruct _cached;
    private Vector2 _cachedMouse;

    private float _shotCharge; // 0..500

    public void BeginFrame(long frameId, float dt, bool isActive)
    {
        if (_frameIdCached == frameId) return;
        _frameIdCached = frameId;

        _cached.Old = _cached.Current;
        _cached.Current = 0;

        // 👇 SE NON È ATTIVA, NON LEGGIAMO INPUT
        if (!isActive)
        {
            _shotCharge = 0f;
            return;
        }

        var k = Keyboard.GetState();

        if (k.IsKeyDown(Keys.W)) _cached.Current |= StateList.Up;
        if (k.IsKeyDown(Keys.S)) _cached.Current |= StateList.Down;
        if (k.IsKeyDown(Keys.A)) _cached.Current |= StateList.Left;
        if (k.IsKeyDown(Keys.D)) _cached.Current |= StateList.Right;
        if (k.IsKeyDown(Keys.R)) _cached.Current |= StateList.Reload;

        var m = Mouse.GetState();
        _cachedMouse = new Vector2(m.X, m.Y);

        bool wasShooting = (_cached.Old & StateList.Shoot) != 0;

        if (m.LeftButton == ButtonState.Pressed)
        {
            if (!wasShooting) _shotCharge = 0f;

            _cached.Current |= StateList.Shoot;

            _shotCharge += dt * 166.67f;
            if (_shotCharge > 500f) _shotCharge = 500f;
        }

        // pulizia opposti
        if ((_cached.Current & StateList.Left) != 0 && (_cached.Current & StateList.Right) != 0)
            _cached.Current &= ~(StateList.Left | StateList.Right);

        if ((_cached.Current & StateList.Up) != 0 && (_cached.Current & StateList.Down) != 0)
            _cached.Current &= ~(StateList.Up | StateList.Down);

        if ((_cached.Current & (StateList.Up | StateList.Down | StateList.Left | StateList.Right)) != 0)
            _cached.Current |= StateList.Moving;
    }

    public Vector2 GetMousePosition() => _cachedMouse;

    public float GetShotCharge() => _shotCharge;

    public void UpdateInput(ref StateStruct inputList)
    {
        inputList.Old = _cached.Old;
        inputList.Current = _cached.Current;
    }
}