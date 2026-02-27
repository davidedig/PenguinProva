namespace ApocalypseSnow;

/// <summary>
/// Dati associati a uno sparo.
/// 
/// Protocollo:
/// - mouseX / mouseY : int32 (screen space)
/// - charge          : int32 (0..500)
/// 
/// Wire format:
/// [type:1B][mouseX:4B][mouseY:4B][charge:4B]
/// </summary>
public struct ShotStruct
{
    public MessageType Type;

    public int mouseX;
    public int mouseY;

    /// <summary>
    /// Holding time scalato (0..500)
    /// </summary>
    public int charge;

    public ShotStruct(int x, int y, int chargeValue)
    {
        Type = MessageType.Shot;
        mouseX = x;
        mouseY = y;
        charge = chargeValue;
    }
}