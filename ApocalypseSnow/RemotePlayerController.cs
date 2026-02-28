using Microsoft.Xna.Framework;
using System.Threading;

namespace ApocalypseSnow;

public sealed class RemotePlayerController : IPlayerController
{
    private StateStruct _state;

    // shot data
    private Vector2 _receivedOffset;
    private float _receivedCharge;

    // pending input state from network
    private StateList _pendingMask;
    private int _hasPendingMask;

    // pending shot from network
    private Vector2 _pendingOffset;
    private float _pendingShotCharge;
    private int _hasPendingShot;

    // pending position snapshot from network
    private Vector2 _pendingPos;
    private int _hasPendingPos;

    public Vector2 ShootOffset => _receivedOffset;
    public float ShotCharge => _receivedCharge;

    public void ApplyRemoteState(StateList mask)
    {
        _pendingMask = mask;
        Interlocked.Exchange(ref _hasPendingMask, 1);
    }

    public void ApplyRemoteShot(float x, float y, float charge)
    {
        _pendingOffset = new Vector2(x, y);
        _pendingShotCharge = charge;
        Interlocked.Exchange(ref _hasPendingShot, 1);
    }

    public void ApplyRemotePosition(Vector2 pos)
    {
        _pendingPos = pos;
        Interlocked.Exchange(ref _hasPendingPos, 1);
    }

    public void UpdateInput(ref StateStruct state)
    {
        _state.Old = _state.Current;

        if (Interlocked.Exchange(ref _hasPendingMask, 0) == 1)
        {
            _state.Current = _pendingMask;
        }

        if (Interlocked.Exchange(ref _hasPendingShot, 0) == 1)
        {
            _receivedOffset = _pendingOffset;
            _receivedCharge = _pendingShotCharge;

            // one-tick shoot pulse (JustReleased on the Penguin side)
            _state.Old |= StateList.Shoot;
            _state.Current &= ~StateList.Shoot;
        }

        state.Old = _state.Old;
        state.Current = _state.Current;
    }

    public void UpdatePosition(ref Vector2 position, float dt, in StateStruct state)
    {
        // snapshot-driven: apply last received position
        if (Interlocked.Exchange(ref _hasPendingPos, 0) == 1)
        {
            position = _pendingPos;
        }

        // If you later want smoothing:
        // position = Vector2.Lerp(position, _pendingPos, 0.25f);
        // (but then do not reset _hasPendingPos to 0 immediately)
    }
}