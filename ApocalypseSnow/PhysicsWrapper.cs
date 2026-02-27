using ApocalypseSnow;
using Microsoft.Xna.Framework;
using System;
using System.Runtime.InteropServices;

public static class PhysicsWrapper
{
    // Build string della DLL (comoda e pronta all'uso)
    public static string BuildInfo =>
        Marshal.PtrToStringAnsi(PhysicsAPI.PhysicsBuildInfo()) ?? "(null)";

    public static float UniformMotion(float position, float velocity, float dt)
    {
        PhysicsAPI.uniform_rectilinear_motion(ref position, velocity, dt);
        return position;
    }

    public static Vector2 StepFromState(Vector2 pos, StateList state, float speed, float dt)
    {
        float vx = 0f;
        float vy = 0f;

        if ((state & StateList.Up) != 0) vy -= 1f;
        if ((state & StateList.Down) != 0) vy += 1f;
        if ((state & StateList.Left) != 0) vx -= 1f;
        if ((state & StateList.Right) != 0) vx += 1f;

        // Normalizziazione della velocità (per evitare di muoversi più velocemente in diagonale)
        if (vx != 0f || vy != 0f)
            PhysicsAPI.normalizeVelocity(ref vx, ref vy);

        vx *= speed;
        vy *= speed;

        pos.X = UniformMotion(pos.X, vx, dt);
        pos.Y = UniformMotion(pos.Y, vy, dt);

        return pos;
    }

    public static Vector2 ParabolicMotion(
    float gravity,
    Vector2 startPosition,
    Vector2 startVelocity,
    float time)
{
    PhysicsAPI.parabolic_motion(
        gravity,
        startPosition.X,
        startPosition.Y,
        out float x,
        out float y,
        startVelocity.X,
        startVelocity.Y,
        time
    );

    return new Vector2(x, y);
}
}