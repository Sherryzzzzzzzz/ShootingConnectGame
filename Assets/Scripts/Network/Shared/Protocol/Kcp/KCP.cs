// KCP — A Better ARQ Protocol Implementation (C# port)
// skywind3000 (C original), limpo1989 + community (C# port)
// Licensed under MIT

using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.Protocol.Kcp
{
    public class KCP
    {
        public const int IKCP_RTO_NDL = 30;   // no delay min rto
        public const int IKCP_RTO_MIN = 100;  // normal min rto
        public const int IKCP_RTO_DEF = 200;
        public const int IKCP_RTO_MAX = 60000;
        public const int IKCP_CMD_PUSH = 81;  // cmd: push data
        public const int IKCP_CMD_ACK = 82;   // cmd: ack
        public const int IKCP_CMD_WASK = 83;  // cmd: window probe (ask)
        public const int IKCP_CMD_WINS = 84;  // cmd: window size (tell)
        public const int IKCP_ASK_SEND = 1;   // need to send IKCP_CMD_WASK
        public const int IKCP_ASK_TELL = 2;   // need to send IKCP_CMD_WINS
        public const int IKCP_WND_SND = 32;
        public const int IKCP_WND_RCV = 128;
        public const int IKCP_MTU_DEF = 1400;
        public const int IKCP_ACK_FAST = 3;
        public const int IKCP_INTERVAL = 100;
        public const int IKCP_OVERHEAD = 26;
        public const int IKCP_DEADLINK = 20;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN = 2;
        public const int IKCP_PROBE_INIT = 7000;   // 7 secs to probe window size
        public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window

        // Encode / decode bytes to/from uint (little-endian)
        public static uint IkcpDecode32u(byte[] p, int offset)
        {
            return (uint)(p[offset] | (p[offset + 1] << 8) | (p[offset + 2] << 16) | (p[offset + 3] << 24));
        }

        public static void IkcpEncode32u(byte[] p, int offset, uint v)
        {
            p[offset] = (byte)(v & 0xFF);
            p[offset + 1] = (byte)((v >> 8) & 0xFF);
            p[offset + 2] = (byte)((v >> 16) & 0xFF);
            p[offset + 3] = (byte)((v >> 24) & 0xFF);
        }

        // Compare sequence numbers (wrapping-aware). Returns negative if later < earlier.
        static int IkcpItimediff(uint later, uint earlier) => (int)(later - earlier);

        struct Segment
        {
            public uint Conv;
            public uint Cmd;
            public uint Frg;
            public uint Wnd;
            public uint Ts;
            public uint Sn;
            public uint Una;
            public uint Rto;
            public uint Xmit;
            public uint ResendTs;
            public uint FastAck;
            public uint AckedCount;
            public byte[] Data;
            public int DataLen;

            public Segment(int size)
            {
                Conv = 0; Cmd = 0; Frg = 0; Wnd = 0; Ts = 0; Sn = 0; Una = 0;
                Rto = 0; Xmit = 0; ResendTs = 0; FastAck = 0; AckedCount = 0;
                Data = new byte[size];
                DataLen = 0;
            }

            public int Encode(byte[] buf, int offset)
            {
                int start = offset;
                IkcpEncode32u(buf, offset, Conv); offset += 4;
                buf[offset++] = (byte)Cmd;
                buf[offset++] = (byte)Frg;
                IkcpEncode32u(buf, offset, (uint)Wnd); offset += 4;
                IkcpEncode32u(buf, offset, Ts); offset += 4;
                IkcpEncode32u(buf, offset, Sn); offset += 4;
                IkcpEncode32u(buf, offset, Una); offset += 4;
                IkcpEncode32u(buf, offset, (uint)DataLen); offset += 4;
                return IKCP_OVERHEAD;
            }
        }

        uint _conv;
        uint _mtu, _mss;
        uint _state;
        uint _snd_una, _snd_nxt, _rcv_nxt;
        uint _ts_recent, _ts_lastack, _ssthresh;
        int _rx_rttval, _rx_srtt, _rx_rto, _rx_minrto;
        uint _snd_wnd, _rcv_wnd, _rmt_wnd, _cwnd, _probe;
        uint _current;
        uint _interval, _ts_flush, _xmit;
        uint _nodelay, _updated;
        uint _ts_probe, _probe_wait;
        uint _dead_link, _incr;

        List<Segment> _snd_queue = new List<Segment>();
        List<Segment> _rcv_queue = new List<Segment>();
        List<Segment> _snd_buf = new List<Segment>();
        List<Segment> _rcv_buf = new List<Segment>();

        List<uint> _acklist = new List<uint>();
        byte[] _buffer;
        int _bufferHead;
        Action<byte[], int> _output;

        uint _fastResend;
        uint _fastLimit;
        bool _nodelayFlag;
        int _rx_rtoCount;

        // ---- Public API ----

        /// <summary>Create a KCP instance with conversation ID and output callback</summary>
        public KCP(uint conv, Action<byte[], int> output)
        {
            _conv = conv;
            _output = output;
            _snd_wnd = IKCP_WND_SND;
            _rcv_wnd = IKCP_WND_RCV;
            _rmt_wnd = IKCP_WND_RCV;
            _mtu = IKCP_MTU_DEF;
            _mss = _mtu - IKCP_OVERHEAD;
            _buffer = new byte[(_mtu + IKCP_OVERHEAD) * 3];
            _rx_rto = IKCP_RTO_DEF;
            _rx_minrto = IKCP_RTO_MIN;
            _interval = IKCP_INTERVAL;
            _ts_flush = IKCP_INTERVAL;
            _ssthresh = IKCP_THRESH_INIT;
            _dead_link = IKCP_DEADLINK;
        }

        /// <summary>Receive data from KCP. Returns negative if no data.</summary>
        public int Recv(byte[] buffer)
        {
            if (_rcv_queue.Count == 0) return -1;

            int peekSize = PeekSize();
            if (peekSize < 0) return -2;
            if (peekSize > buffer.Length) return -3;

            bool recovered = _rcv_queue.Count >= _rcv_wnd;
            int pos = 0;

            // Merge all ready segments into buffer
            var removes = new List<int>();
            for (int i = 0; i < _rcv_queue.Count; i++)
            {
                var seg = _rcv_queue[i];
                Buffer.BlockCopy(seg.Data, 0, buffer, pos, seg.DataLen);
                pos += seg.DataLen;
                removes.Add(i);
                if (seg.Frg == 0) break;
            }

            for (int i = removes.Count - 1; i >= 0; i--)
                _rcv_queue.RemoveAt(removes[i]);

            // Move available segments from buf to queue
            while (_rcv_buf.Count > 0)
            {
                var seg = _rcv_buf[0];
                if (seg.Sn == _rcv_nxt && _rcv_queue.Count < _rcv_wnd)
                {
                    _rcv_buf.RemoveAt(0);
                    _rcv_queue.Add(seg);
                    _rcv_nxt++;
                }
                else break;
            }

            // Fast recovery
            if (_rcv_queue.Count < _rcv_wnd && recovered)
            {
                // Need to send WINS
                _probe |= IKCP_ASK_TELL;
            }

            return pos;
        }

        /// <summary>Send data into KCP for reliable delivery.</summary>
        public int Send(byte[] buffer)
        {
            return Send(buffer, 0, buffer.Length);
        }

        public int Send(byte[] buffer, int offset, int len)
        {
            if (len < 0) return -1;

            int count;
            if (len <= _mss)
                count = 1;
            else
                count = (int)((len + _mss - 1) / _mss);

            if (count > 255) return -2;
            if (count == 0) count = 1;

            for (int i = 0; i < count; i++)
            {
                int size = len > (int)_mss ? (int)_mss : len;
                var seg = new Segment(size);
                Buffer.BlockCopy(buffer, offset, seg.Data, 0, size);
                seg.DataLen = size;
                seg.Frg = (uint)(count - i - 1);
                _snd_queue.Add(seg);
                offset += size;
                len -= size;
            }
            return 0;
        }

        /// <summary>Input received raw data to KCP. Returns 0 on success.</summary>
        public int Input(byte[] data)
        {
            return Input(data, 0, data.Length);
        }

        public int Input(byte[] data, int offset, int size)
        {
            uint prev_una = _snd_una;
            uint maxack = 0;
            uint latest_ts = 0;
            int flag = 0;

            if (data == null || size < IKCP_OVERHEAD) return -1;

            while (true)
            {
                if (size < IKCP_OVERHEAD) break;

                uint conv = IkcpDecode32u(data, offset);
                if (conv != _conv) return -1;

                uint cmd = data[offset + 4];
                uint frg = data[offset + 5];
                uint wnd = IkcpDecode32u(data, offset + 6);
                uint ts = IkcpDecode32u(data, offset + 10);
                uint sn = IkcpDecode32u(data, offset + 14);
                uint una = IkcpDecode32u(data, offset + 18);
                uint len = IkcpDecode32u(data, offset + 22);

                size -= IKCP_OVERHEAD;
                offset += IKCP_OVERHEAD;

                if (size < (int)len) return -2;

                switch (cmd)
                {
                    case IKCP_CMD_PUSH:
                    case IKCP_CMD_ACK:
                    case IKCP_CMD_WASK:
                    case IKCP_CMD_WINS:
                        break;
                    default:
                        return -3;
                }

                _rmt_wnd = wnd;
                ParseUna(una);
                ShrinkBuf();

                if (cmd == IKCP_CMD_ACK)
                {
                    if (IkcpItimediff(_current, ts) >= 0)
                        UpdateAck((int)(_current - ts));
                    ParseAck(sn);
                    ShrinkBuf();
                    if (flag == 0)
                    {
                        flag = 1;
                        maxack = sn;
                        latest_ts = ts;
                    }
                    else
                    {
                        if (IkcpItimediff(sn, maxack) > 0)
                        {
                            maxack = sn;
                            latest_ts = ts;
                        }
                    }
                }
                else if (cmd == IKCP_CMD_PUSH)
                {
                    if (IkcpItimediff(sn, _rcv_nxt + _rcv_wnd) < 0)
                    {
                        AckPush(sn, ts);
                        if (IkcpItimediff(sn, _rcv_nxt) >= 0)
                        {
                            var seg = new Segment((int)len);
                            seg.Conv = conv;
                            seg.Cmd = cmd;
                            seg.Frg = frg;
                            seg.Wnd = wnd;
                            seg.Ts = ts;
                            seg.Sn = sn;
                            seg.Una = una;
                            seg.DataLen = (int)len;
                            if (len > 0)
                                Buffer.BlockCopy(data, offset, seg.Data, 0, (int)len);
                            ParseData(seg);
                        }
                    }
                }
                else if (cmd == IKCP_CMD_WASK)
                {
                    _probe |= IKCP_ASK_TELL;
                }
                else if (cmd == IKCP_CMD_WINS)
                {
                    // Do nothing
                }
                else return -3;

                offset += (int)len;
                size -= (int)len;
            }

            if (flag != 0)
                ParseFastAck(maxack, latest_ts);

            if (IkcpItimediff(_snd_una, prev_una) > 0)
            {
                if (_cwnd < _rmt_wnd)
                {
                    uint mss = _mss;
                    if (_cwnd < _ssthresh)
                    {
                        _cwnd++;
                        _incr += mss;
                    }
                    else
                    {
                        if (_incr < mss) _incr = mss;
                        _incr += (mss * mss) / _incr + (mss / 16);
                        if ((_cwnd + 1) * mss <= _incr)
                            _cwnd++;
                    }
                    if (_cwnd > _rmt_wnd)
                        _cwnd = _rmt_wnd;
                }
            }

            return 0;
        }

        // Parse una
        void ParseUna(uint una)
        {
            var removes = new List<int>();
            for (int i = 0; i < _snd_buf.Count; i++)
            {
                if (IkcpItimediff(_snd_buf[i].Sn, una) < 0)
                    removes.Add(i);
                else break;
            }
            for (int i = removes.Count - 1; i >= 0; i--)
                _snd_buf.RemoveAt(removes[i]);
        }

        // Shrink send buffer
        void ShrinkBuf()
        {
            if (_snd_buf.Count > 0)
                _snd_una = _snd_buf[0].Sn;
            else
                _snd_una = _snd_nxt;
        }

        // Parse ACK
        void ParseAck(uint sn)
        {
            if (IkcpItimediff(sn, _snd_una) < 0 || IkcpItimediff(sn, _snd_nxt) >= 0) return;

            for (int i = 0; i < _snd_buf.Count; i++)
            {
                var seg = _snd_buf[i];
                if (sn == seg.Sn)
                {
                    _snd_buf.RemoveAt(i);
                    seg.AckedCount = 1;
                    break;
                }
                if (IkcpItimediff(sn, seg.Sn) < 0) break;
            }
        }

        // Get how many bytes are ready to recv
        public int PeekSize()
        {
            if (_rcv_queue.Count == 0) return -1;
            var seq = _rcv_queue[0];
            if (seq.Frg == 0) return seq.DataLen;
            if (_rcv_queue.Count < seq.Frg + 1) return -1;
            int length = 0;
            for (int i = 0; i < _rcv_queue.Count; i++)
            {
                length += _rcv_queue[i].DataLen;
                if (_rcv_queue[i].Frg == 0) break;
            }
            return length;
        }

        void ParseData(Segment newseg)
        {
            uint sn = newseg.Sn;
            if (IkcpItimediff(sn, _rcv_nxt + _rcv_wnd) >= 0) return;

            // Insert into rcv_buf sorted by sn
            int insertIdx = _rcv_buf.Count;
            for (int i = 0; i < _rcv_buf.Count; i++)
            {
                if (_rcv_buf[i].Sn == sn) return; // duplicate
                if (IkcpItimediff(sn, _rcv_buf[i].Sn) < 0)
                {
                    insertIdx = i;
                    break;
                }
            }

            // Insert
            if (insertIdx == _rcv_buf.Count)
                _rcv_buf.Add(newseg);
            else
                _rcv_buf.Insert(insertIdx, newseg);

            // Move ready segments to rcv_queue
            while (_rcv_buf.Count > 0)
            {
                var seg = _rcv_buf[0];
                if (seg.Sn == _rcv_nxt && _rcv_queue.Count < _rcv_wnd)
                {
                    _rcv_buf.RemoveAt(0);
                    _rcv_queue.Add(seg);
                    _rcv_nxt++;
                }
                else break;
            }
        }

        void AckPush(uint sn, uint ts)
        {
            _acklist.Add(sn);
            _acklist.Add(ts);
        }

        void ParseFastAck(uint sn, uint ts)
        {
            if (IkcpItimediff(sn, _snd_una) < 0 || IkcpItimediff(sn, _snd_nxt) >= 0) return;

            for (int i = 0; i < _snd_buf.Count; i++)
            {
                var seg = _snd_buf[i];
                if (IkcpItimediff(sn, seg.Sn) < 0) break;
                if (sn != seg.Sn)
                {
                    seg.FastAck++;
                }
            }
        }

        void UpdateAck(int rtt)
        {
            if (_rx_srtt == 0)
            {
                _rx_srtt = rtt;
                _rx_rttval = rtt / 2;
            }
            else
            {
                int delta = rtt - _rx_srtt;
                if (delta < 0) delta = -delta;
                _rx_rttval = (3 * _rx_rttval + delta) / 4;
                _rx_srtt = (7 * _rx_srtt + rtt) / 8;
                if (_rx_srtt < 1) _rx_srtt = 1;
            }

            int rto = _rx_srtt + System.Math.Max((int)_interval, 4 * _rx_rttval);
            _rx_rto = (int)IkcpBound((uint)_rx_minrto, (uint)rto, IKCP_RTO_MAX);
        }

        static uint IkcpBound(uint lower, uint middle, uint upper)
        {
            return System.Math.Min(System.Math.Max(lower, middle), upper);
        }

        /// <summary>Update KCP state. Call this periodically. Returns next update interval in ms.</summary>
        public void Update(uint current)
        {
            _current = current;

            if (_updated == 0)
            {
                _updated = 1;
                _ts_flush = _current;
            }

            int slap = (int)(_current - _ts_flush);
            if (slap >= 10000 || slap < -10000)
            {
                _ts_flush = _current;
                slap = 0;
            }

            if (slap >= 0)
            {
                _ts_flush += _interval;
                if (IkcpItimediff(_current, _ts_flush) >= 0)
                    _ts_flush = _current + _interval;
                Flush();
            }
        }

        /// <summary>Check KCP state. Returns next update interval in ms.</summary>
        public int Check(uint current)
        {
            _current = current;

            if (_updated == 0)
            {
                _updated = 1;
                _ts_flush = _current;
            }

            int slap = (int)(_current - _ts_flush);

            if (slap >= 10000 || slap < -10000)
            {
                _ts_flush = _current;
                slap = 0;
            }

            if (slap >= 0) return 1;
            return (int)(_ts_flush - _current);
        }

        void Flush()
        {
            if (_updated == 0) return;

            // Check for window probe
            if (_rmt_wnd == 0)
            {
                if (_probe_wait == 0)
                {
                    _probe_wait = IKCP_PROBE_INIT;
                    _ts_probe = _current + _probe_wait;
                }
                else
                {
                    if (IkcpItimediff(_current, _ts_probe) >= 0)
                    {
                        if (_probe_wait < IKCP_PROBE_INIT)
                            _probe_wait = IKCP_PROBE_INIT;
                        _probe_wait += _probe_wait / 2;
                        if (_probe_wait > IKCP_PROBE_LIMIT)
                            _probe_wait = IKCP_PROBE_LIMIT;
                        _ts_probe = _current + _probe_wait;
                        _probe |= IKCP_ASK_SEND;
                    }
                }
            }
            else
            {
                _ts_probe = 0;
                _probe_wait = 0;
            }

            // Flush ACKs
            if (_acklist.Count > 0)
            {
                FlushAcks();
            }

            // Probe window
            if ((_probe & IKCP_ASK_SEND) != 0)
            {
                SendProbe(IKCP_CMD_WASK);
            }

            // Probe window size
            if ((_probe & IKCP_ASK_TELL) != 0)
            {
                SendProbe(IKCP_CMD_WINS);
                _probe &= ~(uint)IKCP_ASK_TELL;
            }

            // Send data
            _cwnd = System.Math.Min(_snd_wnd, _rmt_wnd);
            if (_cwnd == 0) _cwnd = 1;

            // Move from snd_queue to snd_buf
            while (IkcpItimediff(_snd_nxt, _snd_una + _cwnd) < 0)
            {
                if (_snd_queue.Count == 0) break;
                var newseg = _snd_queue[0];
                _snd_queue.RemoveAt(0);
                newseg.Conv = _conv;
                newseg.Cmd = IKCP_CMD_PUSH;
                newseg.Wnd = _rcv_wnd;
                newseg.Ts = _current;
                newseg.Sn = _snd_nxt++;
                newseg.Una = _rcv_nxt;
                newseg.ResendTs = _current;
                newseg.Rto = (uint)_rx_rto;
                newseg.FastAck = 0;
                newseg.Xmit = 0;
                _snd_buf.Add(newseg);
            }

            // Resend / send data
            uint resent = _fastResend;
            uint rtomin = _nodelayFlag ? 0u : (uint)(_rx_rto >> 3);

            for (int i = 0; i < _snd_buf.Count; i++)
            {
                var segment = _snd_buf[i];
                bool needSend = false;

                if (segment.Xmit == 0)
                {
                    needSend = true;
                    segment.Xmit++;
                    segment.Rto = (uint)_rx_rto;
                    segment.ResendTs = _current + segment.Rto + rtomin;
                }
                else if (IkcpItimediff(_current, segment.ResendTs) >= 0)
                {
                    needSend = true;
                    segment.Xmit++;
                    _xmit++;
                    if (!_nodelayFlag)
                        segment.Rto += (uint)System.Math.Max((int)segment.Rto, _rx_rto);
                    else
                    {
                        int step = _nodelayFlag ? (int)(segment.Rto) : (int)(segment.Rto / 2);
                        segment.Rto += (uint)(step / 2);
                    }
                    segment.ResendTs = _current + segment.Rto;
                }
                else if (segment.FastAck >= resent)
                {
                    needSend = true;
                    segment.Xmit++;
                    segment.FastAck = 0;
                    segment.ResendTs = _current + segment.Rto;
                }

                if (needSend)
                {
                    segment.Ts = _current;
                    segment.Wnd = _rcv_wnd;
                    segment.Una = _rcv_nxt;

                    int need = IKCP_OVERHEAD + segment.DataLen;
                    int ptr = _bufferHead;
                    if (ptr + need > _buffer.Length)
                    {
                        // Flush buffer first
                        if (ptr > 0)
                        {
                            _output(_buffer, ptr);
                            _bufferHead = 0;
                        }
                        if (need > _buffer.Length)
                        {
                            // Direct send for oversized
                            byte[] tmp = new byte[need];
                            int hdr = segment.Encode(tmp, 0);
                            Buffer.BlockCopy(segment.Data, 0, tmp, hdr, segment.DataLen);
                            _output(tmp, need);
                            continue;
                        }
                        ptr = 0;
                    }

                    int hdrLen = segment.Encode(_buffer, ptr);
                    Buffer.BlockCopy(segment.Data, 0, _buffer, ptr + hdrLen, segment.DataLen);
                    _bufferHead = ptr + need;
                }
            }

            // Flush remaining buffer
            if (_bufferHead > 0)
            {
                _output(_buffer, _bufferHead);
                _bufferHead = 0;
            }

            // Remove acked segments from snd_buf
            while (_snd_buf.Count > 0)
            {
                var seg = _snd_buf[0];
                if (seg.AckedCount > 0 || (seg.Xmit >= _dead_link && IkcpItimediff(_current, seg.ResendTs) >= 0))
                {
                    if (_snd_buf[0].AckedCount > 0)
                    {
                        // Successfully delivered
                    }
                    _snd_buf.RemoveAt(0);
                }
                else break;
            }
        }

        void FlushAcks()
        {
            for (int i = 0; i < _acklist.Count; i += 2)
            {
                uint sn = _acklist[i];
                uint ts = _acklist[i + 1];

                if (_rcv_nxt > sn + (uint)_rcv_buf.Count) continue;

                int need = IKCP_OVERHEAD;
                int ptr = _bufferHead;
                if (ptr + need > _buffer.Length)
                {
                    if (ptr > 0)
                    {
                        _output(_buffer, ptr);
                        _bufferHead = 0;
                    }
                    ptr = 0;
                }

                var seg = new Segment(0);
                seg.Conv = _conv;
                seg.Cmd = IKCP_CMD_ACK;
                seg.Wnd = _rcv_wnd;
                seg.Ts = ts;
                seg.Sn = sn;
                seg.Una = _rcv_nxt;
                seg.Encode(_buffer, ptr);
                _bufferHead = ptr + need;
            }
            _acklist.Clear();
        }

        void SendProbe(uint cmd)
        {
            int need = IKCP_OVERHEAD;
            int ptr = _bufferHead;
            if (ptr + need > _buffer.Length)
            {
                if (ptr > 0)
                {
                    _output(_buffer, ptr);
                    _bufferHead = 0;
                }
                ptr = 0;
            }

            var seg = new Segment(0);
            seg.Conv = _conv;
            seg.Cmd = cmd;
            seg.Wnd = _rcv_wnd;
            seg.Ts = 0;
            seg.Sn = 0;
            seg.Una = _rcv_nxt;
            seg.Encode(_buffer, ptr);
            _bufferHead = ptr + need;
        }

        // ---- Configuration ----

        /// <summary>Configure KCP for low-latency or normal mode.</summary>
        /// <param name="nodelay">0=disable, 1=enable</param>
        /// <param name="interval">Internal update interval in ms (e.g. 10)</param>
        /// <param name="resend">Fast resend threshold (0=disable, 2=recommended)</param>
        /// <param name="nc">Flow control (0=disable, 1=enable)</param>
        public void NoDelay(int nodelay, int interval, int resend, int nc)
        {
            _nodelayFlag = nodelay != 0;
            if (nodelay != 0)
            {
                _rx_minrto = IKCP_RTO_NDL;
            }
            else
            {
                _rx_minrto = IKCP_RTO_MIN;
            }

            if (interval >= 0)
            {
                if (interval > 5000) interval = 5000;
                else if (interval < 10) interval = 10;
                _interval = (uint)interval;
            }

            if (resend >= 0)
            {
                _fastResend = (uint)resend;
            }

            if (nc >= 0)
            {
                _fastLimit = (uint)nc;
            }
        }

        /// <summary>Set window sizes.</summary>
        public void WndSize(int sndwnd, int rcvwnd)
        {
            if (sndwnd > 0) _snd_wnd = (uint)sndwnd;
            if (rcvwnd > 0) _rcv_wnd = (uint)System.Math.Max((int)IKCP_WND_RCV, rcvwnd);
        }

        /// <summary>Set maximum transmission unit.</summary>
        public int SetMtu(int mtu)
        {
            if (mtu < 50 || mtu < IKCP_OVERHEAD) return -1;
            _mtu = (uint)mtu;
            _mss = _mtu - IKCP_OVERHEAD;
            _buffer = new byte[(_mtu + IKCP_OVERHEAD) * 3];
            return 0;
        }

        /// <summary>Get conversation ID.</summary>
        public uint Conv => _conv;

        /// <summary>Peek size of next ready message. Returns -1 if no data.</summary>
        public int PeekSizeUnchecked()
        {
            return PeekSize();
        }

        /// <summary>Get current RTO in ms.</summary>
        public int Rto => _rx_rto;

        /// <summary>Get send window.</summary>
        public uint SndWnd => _snd_wnd;

        /// <summary>Get total retransmit count.</summary>
        public uint XmitCount => _xmit;

        /// <summary>Set minimum RTO.</summary>
        public void SetMinRto(int minRto)
        {
            _rx_minrto = minRto;
        }

        /// <summary>Maximum send queue segments pending.</summary>
        public int WaitSnd => _snd_buf.Count + _snd_queue.Count;
    }
}
