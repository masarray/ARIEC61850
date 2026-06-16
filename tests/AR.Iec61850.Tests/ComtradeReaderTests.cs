using System.Buffers.Binary;
using AR.Iec61850.Comtrade;

namespace AR.Iec61850.Tests;

public sealed class ComtradeReaderTests
{
    [Fact]
    public void ReaderLoadsAsciiComtrade()
    {
        using var temp = new TempComtradeDirectory();
        temp.WriteConfig("ASCII");
        temp.WriteAsciiData();

        var dataset = new ComtradeReader().Load(temp.ConfigPath);

        Assert.Equal("ASCII", dataset.Configuration.DataFileType);
        Assert.Equal(2, dataset.SampleCount);
        Assert.Equal(100.0, dataset.Samples[0].AnalogValues[0]);
        Assert.Equal(-5.0, dataset.Samples[1].AnalogValues[1]);
    }

    [Fact]
    public void ReaderLoadsBinaryComtrade()
    {
        using var temp = new TempComtradeDirectory();
        temp.WriteConfig("BINARY");
        temp.WriteBinaryData();

        var dataset = new ComtradeReader().Load(temp.ConfigPath);

        Assert.Equal("BINARY", dataset.Configuration.DataFileType);
        Assert.Equal(2, dataset.SampleCount);
        Assert.Equal(100.0, dataset.Samples[0].AnalogValues[0]);
        Assert.Equal(-5.0, dataset.Samples[1].AnalogValues[1]);
        Assert.True(dataset.NominalSampleRateHz > 0);
    }

    private sealed class TempComtradeDirectory : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ar-comtrade-" + Guid.NewGuid().ToString("N"));

        public TempComtradeDirectory()
        {
            Directory.CreateDirectory(_directory);
        }

        public string ConfigPath => Path.Combine(_directory, "sample.cfg");
        public string DataPath => Path.Combine(_directory, "sample.dat");

        public void WriteConfig(string dataType)
        {
            File.WriteAllText(ConfigPath, string.Join(Environment.NewLine,
            [
                "TEST,AR,2013",
                "2,2A,0D",
                "1,VA,A,,V,0.01,0,0,-32768,32767,1,1,P",
                "2,IA,A,,A,0.001,0,0,-32768,32767,1,1,P",
                "50",
                "1",
                "4000,2",
                "01/01/2026,00:00:00.000000",
                "01/01/2026,00:00:00.000000",
                dataType,
                "1"
            ]));
        }

        public void WriteAsciiData()
        {
            File.WriteAllText(DataPath, string.Join(Environment.NewLine,
            [
                "1,0,10000,1000",
                "2,250,9000,-5000"
            ]));
        }

        public void WriteBinaryData()
        {
            Span<byte> data = stackalloc byte[2 * 12];
            var offset = 0;
            WriteRecord(data, ref offset, 1, 0, 10000, 1000);
            WriteRecord(data, ref offset, 2, 250, 9000, -5000);
            File.WriteAllBytes(DataPath, data.ToArray());
        }

        private static void WriteRecord(Span<byte> data, ref int offset, int number, int timestamp, int va, int ia)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), number);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), timestamp);
            offset += 4;
            BinaryPrimitives.WriteInt16LittleEndian(data.Slice(offset, 2), checked((short)va));
            offset += 2;
            BinaryPrimitives.WriteInt16LittleEndian(data.Slice(offset, 2), checked((short)ia));
            offset += 2;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
