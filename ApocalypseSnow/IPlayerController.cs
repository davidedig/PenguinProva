using Microsoft.Xna.Framework;

namespace ApocalypseSnow;

public interface IPlayerController
{
    Vector2 ShootOffset { get; }
    float ShotCharge { get; }

    void UpdateInput(ref StateStruct state);
    void UpdatePosition(ref Vector2 position, float dt, in StateStruct state);
}