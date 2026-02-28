namespace ApocalypseSnow;

public struct ShotStruct
{
    public MessageType Type;
    public float dirX;
    public float dirY;
    public float charge;

    public ShotStruct(float x, float y, float chargeValue)
    {
        Type = MessageType.Shot;
        dirX = x;
        dirY = y;
        charge = chargeValue;
    }
}