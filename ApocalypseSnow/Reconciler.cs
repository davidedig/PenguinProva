using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ApocalypseSnow;

/// <summary>
/// Client-side reconciler: mantiene gli input pendenti (solo movimento),
/// riceve lo stato autoritativo (ack + pos) e corregge con:
/// - dead-zone (ignora errori piccolissimi)
/// - soft correction (lerp) per errori medi
/// - snap + replay per errori grandi
/// </summary>
public sealed class Reconciler
{
    private struct Pending
    {
        public uint Seq;
        public StateList MoveMask; // SOLO UDLR ripulito
    }

    private readonly List<Pending> _pending = new(capacity: 256);
    private uint _nextSeq = 1;

    private bool _hasAuth;
    private uint _ack;
    private Vector2 _authPos;

    /// <summary>Resetta stato e buffer (utile a inizio partita o dopo reconnessione).</summary>
    public void Reset()
    {
        _pending.Clear();
        _nextSeq = 1;
        _hasAuth = false;
        _ack = 0;
        _authPos = Vector2.Zero;
    }

    public uint NextSeq() => _nextSeq++;

    public void Record(uint seq, StateList moveMask)
    {
        _pending.Add(new Pending { Seq = seq, MoveMask = moveMask });
    }

    public void OnServerAuth(uint ack, Vector2 serverPos)
    {
        _ack = ack;
        _authPos = serverPos;
        _hasAuth = true;
    }

    /// <summary>
    /// Applica reconcile alla posizione locale.
    /// Va chiamato sul main thread (Update), dopo aver eventualmente ricevuto auth.
    /// </summary>
    public void Apply(ref Vector2 pos, float moveSpeed, float moveDt)
    {
        if (!_hasAuth) return;
        _hasAuth = false;

        // Sempre: drop input già confermati (così non cresce all’infinito)
        _pending.RemoveAll(p => p.Seq <= _ack);

        // Tunabili (pixel)
        const float Eps = 0.75f;          // sotto questo, ignora (nessuna correzione)
        const float SnapThreshold = 12f;  // sopra questo, snap + replay
        const float SoftLerp = 0.35f;     // quanto “tirare” verso serverPos quando errore medio

        float err = Vector2.Distance(pos, _authPos);

        if (err <= Eps)
        {
            // praticamente uguale
            return;
        }

        if (err <= SnapThreshold)
        {
            // correzione morbida, niente replay (evita micro-teleport)
            pos = Vector2.Lerp(pos, _authPos, SoftLerp);
            return;
        }

        // errore grande: snap + replay
        Vector2 replayPos = _authPos;

        foreach (var p in _pending)
            replayPos = PhysicsWrapper.StepFromState(replayPos, p.MoveMask, moveSpeed, moveDt);

        pos = replayPos;
    }
}