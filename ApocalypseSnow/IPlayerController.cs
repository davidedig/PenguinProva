using Microsoft.Xna.Framework;

namespace ApocalypseSnow;

public interface IPlayerController
{
    Vector2 GetMousePosition();
    void UpdateInput(ref StateStruct inputList);

    // 0..500 (stessa scala del tuo tiro)
    float GetShotCharge();
}