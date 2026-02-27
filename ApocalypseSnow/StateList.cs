using System;

namespace ApocalypseSnow;

/// <summary>
/// Bitmask degli input del player.
/// 
/// Viene serializzato come int32 nel pacchetto MsgState.
/// Permette di combinare più input usando OR bitwise.
/// 
/// Esempio:
/// mask = Up | Left
/// </summary>
[Flags]
public enum StateList : int
{
    None = 0,

    // Movimento (WASD)
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,

    // Azioni
    Reload = 1 << 4,
    Shoot = 1 << 5,

    // Stato derivato (opzionale)
    Moving = 1 << 6
}