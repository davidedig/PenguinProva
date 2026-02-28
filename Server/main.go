package main

/*
#cgo CFLAGS: -I../Shared
#cgo LDFLAGS: -L../Shared -l:libPhysicsDll.dll
#include "library.h"
*/
import "C"

import (
	"encoding/binary"
	"fmt"
	"io"
	"math"
	"net"
	"sync/atomic"
	"time"
)

const (
	MsgState       = 1 // 9B  [type][mask:int32][seq:uint32]
	MsgShot        = 2 // 13B [type][dirX:float32][dirY:float32][charge:float32]
	MsgJoin        = 3 // 9B  [type][0][0]
	MsgAuthState   = 4 // 13B [type][ack:uint32][x:float32][y:float32]
	MsgJoinAck     = 5 // 13B [type][playerId:uint32][spawnX:float32][spawnY:float32]
	MsgRemoteState = 6 // 13B [type][x:float32][y:float32][mask:int32]
	MsgRemoteShot  = 7 // 13B [type][dirX:float32][dirY:float32][charge:float32]
)

const (
	Up     int32 = 1 << 0
	Down   int32 = 1 << 1
	Left   int32 = 1 << 2
	Right  int32 = 1 << 3
	Reload int32 = 1 << 4
	Shoot  int32 = 1 << 5
)

const (
	MoveHz     float32 = 30.0
	MoveDt     float32 = 1.0 / MoveHz
	MoveSpeed  float32 = 200.0
	ListenAddr         = ":8080"
)

const (
	chargeCap         float32 = 500.0
	maxCatchupPerTick         = 8 // ? max step per tick per client (evita warp se backlog enorme)
)

type Client struct {
	conn  net.Conn
	send  chan []byte
	input chan StateMsg // bounded buffer
	shot  chan ShotMsg  // bounded buffer (release event)
	done  chan struct{}
	id    uint32
	slot  int
}

type StateMsg struct {
	mask int32
	seq  uint32
}

type ShotMsg struct {
	dirX   float32
	dirY   float32
	charge float32
}

type Session struct {
	a, b *Client

	ax, ay float32
	bx, by float32

	aMask int32
	bMask int32

	aAck uint32
	bAck uint32

	tick *time.Ticker
}

var globalID uint32 = 0

func nextID() uint32 { return atomic.AddUint32(&globalID, 1) }

func sanitizeMask(m int32) int32 {
	if (m&Left) != 0 && (m&Right) != 0 {
		m &^= (Left | Right)
	}
	if (m&Up) != 0 && (m&Down) != 0 {
		m &^= (Up | Down)
	}
	return m
}

func stepFromStateDLL(x, y float32, mask int32) (float32, float32) {
	if (mask & Reload) != 0 {
		return x, y
	}

	var vx C.float = 0
	var vy C.float = 0

	if (mask & Up) != 0 {
		vy -= 1
	}
	if (mask & Down) != 0 {
		vy += 1
	}
	if (mask & Left) != 0 {
		vx -= 1
	}
	if (mask & Right) != 0 {
		vx += 1
	}

	if vx != 0 || vy != 0 {
		C.normalizeVelocity(&vx, &vy)
	}

	vx *= C.float(MoveSpeed)
	vy *= C.float(MoveSpeed)

	cx := C.float(x)
	cy := C.float(y)

	C.uniform_rectilinear_motion(&cx, vx, C.float(MoveDt))
	C.uniform_rectilinear_motion(&cy, vy, C.float(MoveDt))

	return float32(cx), float32(cy)
}

func sendJoinAck(c *Client, playerId uint32, spawnX, spawnY float32) {
	buf := make([]byte, 13)
	buf[0] = MsgJoinAck
	binary.LittleEndian.PutUint32(buf[1:5], playerId)
	binary.LittleEndian.PutUint32(buf[5:9], math.Float32bits(spawnX))
	binary.LittleEndian.PutUint32(buf[9:13], math.Float32bits(spawnY))
	trySend(c, buf, true)
}

func sendAuthStateSelf(c *Client, ackSeq uint32, x, y float32) {
	buf := make([]byte, 13)
	buf[0] = MsgAuthState
	binary.LittleEndian.PutUint32(buf[1:5], ackSeq)
	binary.LittleEndian.PutUint32(buf[5:9], math.Float32bits(x))
	binary.LittleEndian.PutUint32(buf[9:13], math.Float32bits(y))
	trySend(c, buf, false)
}

func sendRemoteState(c *Client, x, y float32, mask int32) {
	buf := make([]byte, 13)
	buf[0] = MsgRemoteState
	binary.LittleEndian.PutUint32(buf[1:5], math.Float32bits(x))
	binary.LittleEndian.PutUint32(buf[5:9], math.Float32bits(y))
	binary.LittleEndian.PutUint32(buf[9:13], uint32(mask))
	trySend(c, buf, false)
}

func sendRemoteShot(c *Client, dx, dy, charge float32) {
	if charge < 0 {
		charge = 0
	}
	if charge > chargeCap {
		charge = chargeCap
	}

	buf := make([]byte, 13)
	buf[0] = MsgRemoteShot
	binary.LittleEndian.PutUint32(buf[1:5], math.Float32bits(dx))
	binary.LittleEndian.PutUint32(buf[5:9], math.Float32bits(dy))
	binary.LittleEndian.PutUint32(buf[9:13], math.Float32bits(charge))
	trySend(c, buf, false)
}

func trySend(c *Client, pkt []byte, mustSend bool) {
	select {
	case c.send <- pkt:
	default:
		if mustSend {
			closeClient(c)
		}
	}
}

func closeClient(c *Client) {
	select {
	case <-c.done:
		return
	default:
		close(c.done)
		_ = c.conn.Close()
	}
}

func writerLoop(c *Client) {
	defer closeClient(c)
	for {
		select {
		case <-c.done:
			return
		case pkt := <-c.send:
			if pkt == nil {
				return
			}
			_, err := c.conn.Write(pkt)
			if err != nil {
				return
			}
		}
	}
}

func readExactly(conn net.Conn, n int) ([]byte, error) {
	buf := make([]byte, n)
	_, err := io.ReadFull(conn, buf)
	return buf, err
}

func readerLoop(c *Client) {
	defer closeClient(c)
	for {
		h, err := readExactly(c.conn, 1)
		if err != nil {
			return
		}
		typ := h[0]

		switch typ {
		case MsgJoin:
			if _, err := readExactly(c.conn, 8); err != nil {
				return
			}

		case MsgState:
			pl, err := readExactly(c.conn, 8)
			if err != nil {
				return
			}
			mask := int32(binary.LittleEndian.Uint32(pl[0:4]))
			mask = sanitizeMask(mask)
			seq := binary.LittleEndian.Uint32(pl[4:8])

			msg := StateMsg{mask: mask, seq: seq}

			// bounded buffer, non bloccare mai
			select {
			case c.input <- msg:
			default:
				select { case <-c.input: default: }
				select { case c.input <- msg: default: }
			}

		case MsgShot:
			pl, err := readExactly(c.conn, 12)
			if err != nil {
				return
			}
			dx := math.Float32frombits(binary.LittleEndian.Uint32(pl[0:4]))
			dy := math.Float32frombits(binary.LittleEndian.Uint32(pl[4:8]))
			ch := math.Float32frombits(binary.LittleEndian.Uint32(pl[8:12]))

			msg := ShotMsg{dirX: dx, dirY: dy, charge: ch}

			select {
			case c.shot <- msg:
			default:
				select { case <-c.shot: default: }
				select { case c.shot <- msg: default: }
			}

		default:
			fmt.Printf("[WARN] unknown type=%d from %v\n", typ, c.conn.RemoteAddr())
			return
		}
	}
}

func matchmaker(join <-chan *Client) {
	var waiting *Client
	for c := range join {
		if waiting == nil {
			waiting = c
			continue
		}
		a := waiting
		b := c
		waiting = nil
		s := newSession(a, b)
		go s.run()
	}
}

func newSession(a, b *Client) *Session {
	s := &Session{
		a:    a,
		b:    b,
		tick: time.NewTicker(time.Second / time.Duration(MoveHz)),
	}
	s.ax, s.ay = 400, 500
	s.bx, s.by = 400, 100
	a.id, b.id = nextID(), nextID()
	a.slot, b.slot = 0, 1
	return s
}

// ? KEY FIX: consuma TUTTI i pacchetti State disponibili e fa 1 step DLL per ciascuno.
// Ritorna (stepsSimulati, x,y, curMask, curAck)
func processClientTicks(c *Client, x, y float32, curMask int32, curAck uint32) (int, float32, float32, int32, uint32) {
	steps := 0
	for {
		select {
		case m := <-c.input:
			curMask = m.mask
			curAck = m.seq
			x, y = stepFromStateDLL(x, y, curMask)
			steps++
			if steps >= maxCatchupPerTick {
				return steps, x, y, curMask, curAck
			}
		default:
			return steps, x, y, curMask, curAck
		}
	}
}

func (s *Session) run() {
	defer s.tick.Stop()
	defer closeClient(s.a)
	defer closeClient(s.b)

	fmt.Printf("[SESSION] start A=%v id=%d | B=%v id=%d\n",
		s.a.conn.RemoteAddr(), s.a.id, s.b.conn.RemoteAddr(), s.b.id)

	sendJoinAck(s.a, s.a.id, s.ax, s.ay)
	sendJoinAck(s.b, s.b.id, s.bx, s.by)

	for {
		select {
		case <-s.a.done:
			return
		case <-s.b.done:
			return
		case <-s.tick.C:
	// ? SOLO input-driven: 1 seq = 1 step
	_, s.ax, s.ay, s.aMask, s.aAck = processClientTicks(s.a, s.ax, s.ay, s.aMask, s.aAck)
	_, s.bx, s.by, s.bMask, s.bAck = processClientTicks(s.b, s.bx, s.by, s.bMask, s.bAck)

	sendAuthStateSelf(s.a, s.aAck, s.ax, s.ay)
	sendRemoteState(s.a, s.bx, s.by, s.bMask)
	sendAuthStateSelf(s.b, s.bAck, s.bx, s.by)
	sendRemoteState(s.b, s.ax, s.ay, s.aMask)

	forwardShots(s.a, s.b)
	forwardShots(s.b, s.a)
		}
	}
}

// (non usata col fix, la lascio solo per riferimento)
func drainState(c *Client, curMask int32, curAck uint32) (int32, uint32) {
	for {
		select {
		case m := <-c.input:
			curMask, curAck = m.mask, m.seq
		default:
			return curMask, curAck
		}
	}
}

func forwardShots(from, to *Client) {
	for {
		select {
		case sh := <-from.shot:
			sendRemoteShot(to, sh.dirX, sh.dirY, sh.charge)
		default:
			return
		}
	}
}

func main() {
	ln, err := net.Listen("tcp", ListenAddr)
	if err != nil {
		panic(err)
	}
	defer ln.Close()

	fmt.Println("Server listening on", ListenAddr)
	join := make(chan *Client, 128)
	go matchmaker(join)

	for {
		conn, err := ln.Accept()
		if err != nil {
			continue
		}
		if tc, ok := conn.(*net.TCPConn); ok {
			_ = tc.SetNoDelay(true)
		}

		c := &Client{
			conn:  conn,
			send:  make(chan []byte, 256),
			input: make(chan StateMsg, 64),
			shot:  make(chan ShotMsg, 8),
			done:  make(chan struct{}),
		}

		fmt.Println("[ACCEPT]", conn.RemoteAddr())
		go writerLoop(c)
		go readerLoop(c)
		join <- c
	}
}