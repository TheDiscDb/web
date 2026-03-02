using TheDiscDb.Web.Barcode;

namespace TheDiscDb.UnitTests.Server.Barcode;

public class Code128EncoderTests
{
    [Test]
    public async Task Encode_NumericData_ProducesBinaryString()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("12345678");

        await Assert.That(result).IsNotNull();
        // Result should be a string of 1s and 0s
        await Assert.That(result.All(c => c == '0' || c == '1')).IsTrue();
    }

    [Test]
    public async Task Encode_AlphanumericData_ProducesBinaryString()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("ABC123");

        await Assert.That(result).IsNotNull();
        await Assert.That(result.All(c => c == '0' || c == '1')).IsTrue();
    }

    [Test]
    public async Task Encode_IncludesTerminationBars()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("TEST");

        // Code128 always ends with "11" termination bars
        await Assert.That(result).EndsWith("11");
    }

    [Test]
    public async Task Encode_IncludesStopPattern()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("TEST");

        // Stop pattern is "11000111010" followed by "11" termination
        await Assert.That(result).EndsWith("1100011101011");
    }

    [Test]
    public async Task Encode_NumericOnly_UsesCodeC()
    {
        var encoder = new Code128Encoder { Type = Code128Type.C };
        var result = encoder.Encode("1234");

        await Assert.That(result).IsNotNull();
        // START_C encoding is "11010011100"
        await Assert.That(result).StartsWith("11010011100");
    }

    [Test]
    public async Task Encode_CodeC_NonNumeric_ThrowsException()
    {
        var encoder = new Code128Encoder { Type = Code128Type.C };
        Exception? caught = null;
        try
        {
            encoder.Encode("ABC");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task Encode_TypeA_ProducesValidOutput()
    {
        var encoder = new Code128Encoder { Type = Code128Type.A };
        var result = encoder.Encode("HELLO");

        await Assert.That(result).IsNotNull();
        // START_A encoding is "11010000100"
        await Assert.That(result).StartsWith("11010000100");
    }

    [Test]
    public async Task Encode_TypeB_ProducesValidOutput()
    {
        var encoder = new Code128Encoder { Type = Code128Type.B };
        var result = encoder.Encode("Hello");

        await Assert.That(result).IsNotNull();
        // START_B encoding is "11010010000"
        await Assert.That(result).StartsWith("11010010000");
    }

    [Test]
    public async Task Encode_Auto_SelectsAppropriateCodeSet()
    {
        var encoder = new Code128Encoder { Type = Code128Type.Auto };
        // Pure numeric data >= 2 chars should select Code C (START_C = "11010011100")
        var result = encoder.Encode("123456");

        await Assert.That(result).StartsWith("11010011100");
    }

    [Test]
    public async Task Encode_SameInput_ProducesSameOutput()
    {
        var encoder1 = new Code128Encoder();
        var encoder2 = new Code128Encoder();

        var result1 = encoder1.Encode("CONSISTENT");
        var result2 = encoder2.Encode("CONSISTENT");

        await Assert.That(result1).IsEqualTo(result2);
    }

    [Test]
    public async Task Encode_DifferentInputs_ProduceDifferentOutputs()
    {
        var encoder1 = new Code128Encoder();
        var encoder2 = new Code128Encoder();

        var result1 = encoder1.Encode("AAA");
        var result2 = encoder2.Encode("BBB");

        await Assert.That(result1).IsNotEqualTo(result2);
    }

    [Test]
    public async Task Encode_SingleChar_ProducesValidOutput()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("A");

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result.All(c => c == '0' || c == '1')).IsTrue();
    }

    [Test]
    public async Task Encode_SpecialChars_ProducesValidOutput()
    {
        var encoder = new Code128Encoder();
        var result = encoder.Encode("!@#$%");

        await Assert.That(result).IsNotNull();
        await Assert.That(result.All(c => c == '0' || c == '1')).IsTrue();
    }
}
