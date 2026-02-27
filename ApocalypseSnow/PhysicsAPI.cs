using System;
using System.Runtime.InteropServices;

internal static class PhysicsAPI
{
    private const string DllName = "libPhysicsDll.dll";

    // ------------------ BUILD INFO ------------------
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr PhysicsBuildInfo();

    // ------------------ MOTION ------------------
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void uniform_rectilinear_motion(ref float position, float velocity, float deltaTime);

    //------------------ NORMALIZZAZIONE DELLA VELOCITA' (per evitare che in diagonale vada più veloce) ------------------
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void normalizeVelocity(ref float velocityX, ref float velocityY);

    [DllImport("libPhysicsDll.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void parabolic_motion(
    float gravity,
    float start_positionX,
    float start_positionY,
    out float positionX,
    out float positionY,
    float start_velocityX,
    float start_velocityY,
    float gameTime
);
}