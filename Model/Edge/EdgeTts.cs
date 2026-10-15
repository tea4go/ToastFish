using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ToastFish.Model.Edge
{
    /// <summary>
    /// 用 Edge 的在线朗读服务合成语音，返回 MP3 字节。
    /// 手写 WebSocket 握手与分帧：ClientWebSocket 不允许改 User-Agent 这类受限头，
    /// 服务端会因此拒绝握手，所以这里直接用 TcpClient + SslStream 自己收发。
    /// 鉴权依赖 <see cref="SecMsGec"/> 生成的令牌，微软改算法就会失效。
    /// </summary>
    internal static class EdgeTts
    {
        const string HOST = "speech.platform.bing.com";
        const string PATH = "/consumer/speech/synthesize/readaloud/edge/v1";
        const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        const string CHROMIUM_FULL_VERSION = "143.0.3650.75";
        const string OUTPUT_FORMAT = "audio-24khz-48kbitrate-mono-mp3";
        const string ORIGIN = "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold";
        const string USER_AGENT =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

        const int ConnectTimeoutMs = 10000;
        const int IoTimeoutMs = 30000;

        /// <summary>单个 SSML 请求的文本上限（字符）。太长服务端会拒，按句切开分多次请求。</summary>
        const int ChunkSize = 1500;

        /// <summary>
        /// 合成语音。voice 传 Edge 的语音名（如 en-US-AriaNeural），
        /// rate 是界面上的系统语速值（-10 ~ 10），这里换算成 Edge 的百分比。
        /// </summary>
        public static byte[] Synthesize(string text, string voice, int rate)
        {
            List<string> chunks = SplitChunks(RemoveIncompatible(text), ChunkSize);
            if (chunks.Count == 0)
                return new byte[0];

            string rateText = RateText(rate);
            var audio = new MemoryStream();
            using (var conn = new WsConnection())
            {
                conn.Connect();
                conn.SendText(SpeechConfig());
                foreach (string chunk in chunks)
                {
                    conn.SendText(Ssml(chunk, voice, rateText));
                    conn.ReadUntilTurnEnd(audio);
                }
            }
            return audio.ToArray();
        }

        /// <summary>系统语速值换算成 Edge 的百分比。±10 对应 ±100%，界面上只用到 ±8。</summary>
        static string RateText(int rate)
        {
            int percent = rate * 10;
            if (percent < -100) percent = -100;
            if (percent > 100) percent = 100;
            return (percent >= 0 ? "+" : "") + percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        static string SpeechConfig()
        {
            return "X-Timestamp:" + JsDate() + "\r\n" +
                   "Content-Type:application/json; charset=utf-8\r\n" +
                   "Path:speech.config\r\n\r\n" +
                   "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
                   "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
                   "\"outputFormat\":\"" + OUTPUT_FORMAT + "\"}}}}";
        }

        static string Ssml(string text, string voice, string rateText)
        {
            string ssml =
                "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
                "<voice name='" + voice + "'>" +
                "<prosody pitch='+0Hz' rate='" + rateText + "' volume='+0%'>" +
                XmlEscape(text) +
                "</prosody></voice></speak>";

            return "X-RequestId:" + Guid.NewGuid().ToString("N") + "\r\n" +
                   "Content-Type:application/ssml+xml\r\n" +
                   "X-Timestamp:" + JsDate() + "Z\r\n" +  // 末尾这个 Z 是 Edge 自己的 bug，照抄
                   "Path:ssml\r\n\r\n" + ssml;
        }

        static string JsDate()
        {
            return DateTime.UtcNow.ToString(
                "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
                CultureInfo.InvariantCulture);
        }

        /// <summary>服务端不吃这几个控制字符，留着会让整个请求失败。</summary>
        static string RemoveIncompatible(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c <= 8 || c == 11 || c == 12 || (c >= 14 && c <= 31))
                    sb.Append(' ');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>按换行/句末标点切成不超过 max 的若干段，尽量不切断句子。</summary>
        static List<string> SplitChunks(string text, int max)
        {
            var result = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int end = Math.Min(start + max, text.Length);
                if (end < text.Length)
                {
                    int cut = LastBreak(text, start, end);
                    if (cut > start) end = cut;
                }
                string chunk = text.Substring(start, end - start).Trim();
                if (chunk.Length > 0)
                    result.Add(chunk);
                start = end;
            }
            return result;
        }

        static int LastBreak(string text, int start, int end)
        {
            for (int i = end - 1; i > start; i--)
            {
                char c = text[i - 1];
                if (c == '\n' || c == '\r' || c == '.' || c == '!' || c == '?' ||
                    c == ';' || c == '。' || c == '！' || c == '？' || c == '；' || c == ' ')
                    return i;
            }
            return -1;
        }

        static string XmlEscape(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// 一条到朗读服务的 WebSocket 连接。协议上服务端只发不掩码的帧，
        /// 客户端发的帧必须掩码，收帧要处理分片和长度扩展。
        /// </summary>
        private sealed class WsConnection : IDisposable
        {
            TcpClient _tcp;
            SslStream _stream;
            readonly byte[] _one = new byte[1];

            public void Connect()
            {
                _tcp = new TcpClient();
                _tcp.ReceiveTimeout = IoTimeoutMs;
                _tcp.SendTimeout = IoTimeoutMs;

                IAsyncResult ar = _tcp.BeginConnect(HOST, 443, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                {
                    _tcp.Close();
                    throw new IOException("连接 Edge 朗读服务超时");
                }
                _tcp.EndConnect(ar);

                _stream = new SslStream(_tcp.GetStream(), false, (s, cert, chain, err) => true);
                _stream.AuthenticateAsClient(HOST, null, SslProtocols.Tls12, false);
                Handshake();
            }

            void Handshake()
            {
                string url = PATH +
                    "?TrustedClientToken=" + TRUSTED_CLIENT_TOKEN +
                    "&ConnectionId=" + Guid.NewGuid().ToString("N") +
                    "&Sec-MS-GEC=" + SecMsGec.Generate() +
                    "&Sec-MS-GEC-Version=2-" + CHROMIUM_FULL_VERSION;

                var req = new StringBuilder();
                req.Append("GET ").Append(url).Append(" HTTP/1.1\r\n");
                req.Append("Host: ").Append(HOST).Append("\r\n");
                req.Append("Upgrade: websocket\r\n");
                req.Append("Connection: Upgrade\r\n");
                req.Append("Sec-WebSocket-Key: ").Append(Convert.ToBase64String(Guid.NewGuid().ToByteArray())).Append("\r\n");
                req.Append("Sec-WebSocket-Version: 13\r\n");
                req.Append("Origin: ").Append(ORIGIN).Append("\r\n");
                req.Append("Pragma: no-cache\r\n");
                req.Append("Cache-Control: no-cache\r\n");
                req.Append("Accept-Language: en-US,en;q=0.9\r\n");
                req.Append("User-Agent: ").Append(USER_AGENT).Append("\r\n");
                req.Append("Cookie: muid=").Append(Guid.NewGuid().ToString("N").ToUpperInvariant()).Append(";\r\n");
                req.Append("\r\n");

                byte[] bytes = Encoding.ASCII.GetBytes(req.ToString());
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();

                string status = ReadLine();
                while (true)
                {
                    string line = ReadLine();
                    if (line.Length == 0) break;
                }
                if (status.IndexOf(" 101", StringComparison.Ordinal) < 0)
                    throw new IOException("Edge 朗读服务拒绝握手：" + status);
            }

            public void SendText(string message)
            {
                byte[] payload = Encoding.UTF8.GetBytes(message);
                var frame = new MemoryStream();
                frame.WriteByte(0x81);  // FIN + text
                if (payload.Length < 126)
                {
                    frame.WriteByte((byte)(0x80 | payload.Length));
                }
                else if (payload.Length < 65536)
                {
                    frame.WriteByte(0x80 | 126);
                    frame.WriteByte((byte)(payload.Length >> 8));
                    frame.WriteByte((byte)(payload.Length & 0xFF));
                }
                else
                {
                    frame.WriteByte(0x80 | 127);
                    for (int i = 7; i >= 0; i--) frame.WriteByte((byte)((long)payload.Length >> (8 * i)));
                }

                var mask = new byte[4];
                new Random().NextBytes(mask);
                frame.Write(mask, 0, 4);
                byte[] header = frame.ToArray();
                _stream.Write(header, 0, header.Length);

                var masked = new byte[payload.Length];
                for (int i = 0; i < payload.Length; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);
                _stream.Write(masked, 0, masked.Length);
                _stream.Flush();
            }

            /// <summary>读到 turn.end 为止，把二进制帧里的音频追加进 audio。</summary>
            public void ReadUntilTurnEnd(Stream audio)
            {
                while (true)
                {
                    int opcode;
                    byte[] payload;
                    if (!ReadFrame(out opcode, out payload))
                        throw new IOException("Edge 朗读服务提前断开连接");

                    if (opcode == 8)
                        throw new IOException("Edge 朗读服务关闭了连接");
                    if (opcode == 1)
                    {
                        if (Encoding.UTF8.GetString(payload).IndexOf("Path:turn.end", StringComparison.Ordinal) >= 0)
                            return;
                    }
                    else if (opcode == 2 && payload.Length > 2)
                    {
                        // 二进制帧前两字节是头部长度（大端），其后才是音频
                        int headerLength = (payload[0] << 8) | payload[1];
                        if (headerLength + 2 <= payload.Length)
                            audio.Write(payload, headerLength + 2, payload.Length - headerLength - 2);
                    }
                }
            }

            bool ReadFrame(out int opcode, out byte[] payload)
            {
                opcode = 0;
                payload = null;
                int b0 = ReadByte();
                if (b0 < 0) return false;
                int b1 = ReadByte();
                if (b1 < 0) return false;

                opcode = b0 & 0x0F;
                bool fin = (b0 & 0x80) != 0;
                long length = b1 & 0x7F;
                if (length == 126)
                    length = (ReadByteStrict() << 8) | ReadByteStrict();
                else if (length == 127)
                {
                    length = 0;
                    for (int i = 0; i < 8; i++) length = (length << 8) | (uint)ReadByteStrict();
                }
                if ((b1 & 0x80) != 0)
                    for (int i = 0; i < 4; i++) ReadByteStrict();

                payload = ReadExactly((int)length);

                while (!fin)
                {
                    int next0 = ReadByteStrict();
                    int next1 = ReadByteStrict();
                    fin = (next0 & 0x80) != 0;
                    long nextLength = next1 & 0x7F;
                    if (nextLength == 126)
                        nextLength = (ReadByteStrict() << 8) | ReadByteStrict();
                    else if (nextLength == 127)
                    {
                        nextLength = 0;
                        for (int i = 0; i < 8; i++) nextLength = (nextLength << 8) | (uint)ReadByteStrict();
                    }
                    if ((next1 & 0x80) != 0)
                        for (int i = 0; i < 4; i++) ReadByteStrict();

                    byte[] more = ReadExactly((int)nextLength);
                    var merged = new byte[payload.Length + more.Length];
                    Buffer.BlockCopy(payload, 0, merged, 0, payload.Length);
                    Buffer.BlockCopy(more, 0, merged, payload.Length, more.Length);
                    payload = merged;
                }
                return true;
            }

            string ReadLine()
            {
                var sb = new StringBuilder();
                while (true)
                {
                    int b = ReadByte();
                    if (b < 0) break;
                    if (b == '\n') break;
                    if (b != '\r') sb.Append((char)b);
                }
                return sb.ToString();
            }

            int ReadByte()
            {
                return _stream.Read(_one, 0, 1) <= 0 ? -1 : _one[0];
            }

            int ReadByteStrict()
            {
                int b = ReadByte();
                if (b < 0) throw new IOException("Edge 朗读服务提前断开连接");
                return b;
            }

            byte[] ReadExactly(int count)
            {
                var buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = _stream.Read(buffer, offset, count - offset);
                    if (read <= 0) throw new IOException("Edge 朗读服务提前断开连接");
                    offset += read;
                }
                return buffer;
            }

            public void Dispose()
            {
                if (_stream != null) { try { _stream.Dispose(); } catch { } _stream = null; }
                if (_tcp != null) { try { _tcp.Close(); } catch { } _tcp = null; }
            }
        }
    }
}
