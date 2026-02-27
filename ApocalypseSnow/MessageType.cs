/// Client -> Server:
/// - State:  9B  [type][mask:int32][seq:uint32]
/// - Shot:   13B [type][mouseX:int32][mouseY:int32][charge:int32]   (charge ignorato se server-side)
/// - Join:   9B  [type][0][0]
///
/// Server -> Client:
/// - JoinAck:     13B [type][playerId:uint32][spawnX:float32][spawnY:float32]
/// - AuthState:   13B [type][ack:uint32][x:float32][y:float32]                 (SELF)
/// - RemoteState: 13B [type][x:float32][y:float32][mask:int32]                 (OTHER)
/// - RemoteShot:  13B [type][mouseX:int32][mouseY:int32][charge:int32]         (OTHER)
public enum MessageType : byte
{
    State = 1,
    Shot = 2,
    PlayerJoin = 3,
    AuthState = 4,
    JoinAck = 5,
    RemoteState = 6,
    RemoteShot = 7
}