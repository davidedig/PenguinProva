using Microsoft.Xna.Framework;
using System.Threading;

namespace ApocalypseSnow;

public sealed class RemotePlayerController : IPlayerController
{
    private StateStruct _cached;

    // per shot: qui NON è mouse assoluto, è offset dx/dy
    private Vector2 _cachedAimOffset;
    private float _cachedCharge;

    private StateList _pendingMask;
    private int _hasPendingMask;

    private Vector2 _pendingAimOffset;
    private float _pendingShotCharge;
    private int _hasPendingShot;

    public void ApplyRemoteState(StateList mask)
    {
        _pendingMask = mask;
        Interlocked.Exchange(ref _hasPendingMask, 1);
    }

    // a,b = dx,dy (offset)
    public void ApplyRemoteShot(int a, int b, float charge)
    {
        _pendingAimOffset = new Vector2(a, b);
        _pendingShotCharge = charge;
        Interlocked.Exchange(ref _hasPendingShot, 1);
    }

    // Ritorna OFFSET (dx,dy)
    public Vector2 GetMousePosition() => _cachedAimOffset;

    public float GetShotCharge() => _cachedCharge;

    public void UpdateInput(ref StateStruct inputList)
    {
        _cached.Old = _cached.Current;

        if (Interlocked.Exchange(ref _hasPendingMask, 0) == 1)
        {
            _cached.Current = _pendingMask;
        }

        if (Interlocked.Exchange(ref _hasPendingShot, 0) == 1)
        {
            _cachedAimOffset = _pendingAimOffset;
            _cachedCharge = _pendingShotCharge;

            // forza release-edge nel frame dello shot-event
            _cached.Old |= StateList.Shoot;
            _cached.Current &= ~StateList.Shoot;
        }

        inputList.Old = _cached.Old;
        inputList.Current = _cached.Current;
    }
}