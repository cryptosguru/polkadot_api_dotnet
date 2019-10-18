﻿namespace Schnorrkel.Merlin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using StrobeNet;

    public class Transcript
    {
        public Strobe _obj { get; private set; }
        private const string MERLIN_PROTOCOL_LABEL = "Merlin v1.0";

        public override string ToString()
        {
            return _obj?.DebugPrintState();
        }
        private Transcript(Strobe obj)
        {
            _obj = obj.Clone() as Strobe;
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public Transcript(string label)
        {
            _obj = new Strobe(MERLIN_PROTOCOL_LABEL, 128);
            AppendMessage(Encoding.UTF8.GetBytes("dom-sep"), Encoding.UTF8.GetBytes(label));
        }

        public Transcript(byte[] label)
        {
            _obj = new Strobe(MERLIN_PROTOCOL_LABEL, 128);
            AppendMessage(Encoding.UTF8.GetBytes("dom-sep"), label);
        }

        public Transcript Clone()
        {
            return new Transcript(_obj);
        }

        private byte[] EncodeU64(byte[] data)
        {
            byte[] result;
            using (Stream dataStream = new MemoryStream(data, false))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    using (StreamWriter sr = new StreamWriter(stream, Encoding.BigEndianUnicode))
                    {
                        while (dataStream.CanRead)
                        {
                            var major = dataStream.ReadByte();
                            var minor = dataStream.ReadByte();

                            sr.Write(minor + major);
                        }

                        result = stream.ToArray();
                    }
                }
            }

            return result;
        }

        public void MetaAd(byte[] data, bool more)
        {
            var error = _obj.Operate(true, StrobeNet.Enums.Operation.Ad, data, 0, more);
            if (error != null)
            {
                throw new ApplicationException($"{error}");
            }
        }

        public void Ad(byte[] data, bool more)
        {
            var error = _obj.Operate(false, StrobeNet.Enums.Operation.Ad, data, 0, more);
            if (error != null)
            {
                throw new ApplicationException($"{error}");
            }
        }

        public byte[] Prf(int expectedOutput, bool more)
        {
            var result = _obj.Operate(false, StrobeNet.Enums.Operation.Prf, null, expectedOutput, more);
            if (result == null)
            {
                throw new ApplicationException($"{result}");
            }

            return result;
        }

        public void Key(byte[] data, bool more)
        {
            var error = _obj.Operate(false, StrobeNet.Enums.Operation.Key, data, 0, more);
            if (error != null)
            {
                throw new Exception($"{error}");
            }
        }

        public Strobe TranscriptCommit(string sth, byte[] message)
        {
            var obj = new Strobe("Merlin", 128);
            obj.Ad(true, message);
            return obj;
        }

        public void AppendMessage(string label, string message)
        {
            AppendMessage(Encoding.UTF8.GetBytes(label), Encoding.UTF8.GetBytes(message));
        }

        public void AppendMessage(string label, byte[] message)
        {
            AppendMessage(Encoding.UTF8.GetBytes(label), message);
        }

        public void AppendMessage(byte[] label, string message)
        {
            AppendMessage(label, Encoding.UTF8.GetBytes(message));
        }

        public void AppendMessage(byte[] label, byte[] message)
        {
            // var dataLength = message.Length;

            MetaAd(label, false);
            MetaAd(BitConverter.GetBytes(message.Length), true);
            Ad(message, false);
        }

        public void CommitBytes(byte[] label, byte[] message)
        {
            AppendMessage(label, message);
        }

        public void AppendU64(byte[] label, byte[] message)
        {
            AppendMessage(label, EncodeU64(message));
        }

        public void CommitU64(byte[] label, byte[] message)
        {
            AppendU64(label, message);
        }

        public void WitnessBytes(byte[] label, ref byte[] dest, byte[] nonceSeeds, RandomGenerator rng)
        {
            byte[][] ns = new byte[][] { nonceSeeds };
            WitnessBytesRng(label, ref dest, ns, rng);
        }

        public void ChallengeBytes(byte[] label, ref byte[] buffer)
        {
            MetaAd(label, false);
            MetaAd(BitConverter.GetBytes(buffer.Length), true);

            buffer = Prf(buffer.Length, false);
        }

        public TranscriptRngBuilder BuildRng()
        {
            return new TranscriptRngBuilder(Clone());
        }

        public void WitnessBytesRng(byte[] label, ref byte[] dest, byte[][] nonce_seeds, RandomGenerator rng)
        {
            var br = BuildRng();
            foreach(var ns in nonce_seeds)
            {
                br = br.RekeyWithWitnessBytes(label, ns);
            }
            var r = br.Finalize(rng);
            r.FillBytes(ref dest);
        }
    }

    public class TranscriptRngBuilder
    {
        public Transcript _strobe { get; private set; }

        public TranscriptRngBuilder(Transcript strobe)
        {
            _strobe = strobe;
        }

        public TranscriptRngBuilder RekeyWithWitnessBytes(byte[] label, byte[] witness)
        {
            _strobe.MetaAd(label, false);
            _strobe.MetaAd(BitConverter.GetBytes(witness.Length), true);
            _strobe.Key(witness, false);

            return this;
        }

        public TranscriptRng Finalize(RandomGenerator rng)
        {
            var bytes = new byte[32];
            bytes.Initialize();
            rng.FillBytes(ref bytes);

            var newStrobe = _strobe.Clone();
            newStrobe.MetaAd(Encoding.UTF8.GetBytes("rng"), false);
            newStrobe.Key(bytes, false);

            return new TranscriptRng(newStrobe);
        }
    }

    public abstract class RandomGenerator
    {
        public abstract void FillBytes(ref byte[] dst);
    }

    public class TranscriptRng : RandomGenerator
    {
        static Random _rnd;
        public Transcript _strobe { get; private set; }
        private byte[] _strobeBytes;
        private int _pointer;

        public TranscriptRng(Transcript strobe)
        {
            if (_rnd == null)
            {
                _rnd = new Random();
            }

            _strobe = strobe;
            _strobeBytes = Transcript.StringToByteArray(strobe._obj.DebugPrintState());
            _pointer = 0;
        }

        public override void FillBytes(ref byte[] dst)
        {
           _strobe.MetaAd(BitConverter.GetBytes(dst.Length), false);
            dst = _strobe.Prf(dst.Length, false);
        }
    }
}
