using TheDiscDb.Web.Barcode;

namespace TheDiscDb.UnitTests.Server.Barcode;

public class BarcodeEncoderTests
{
    [Test]
    public async Task CheckNumericOnly_PureDigits_ReturnsTrue()
    {
        var encoder = new TestBarcodeEncoder();
        var result = encoder.TestCheckNumericOnly("12345");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CheckNumericOnly_WithLetters_ReturnsFalse()
    {
        var encoder = new TestBarcodeEncoder();
        var result = encoder.TestCheckNumericOnly("123abc");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckNumericOnly_Null_ReturnsFalse()
    {
        var encoder = new TestBarcodeEncoder();
        var result = encoder.TestCheckNumericOnly(null!);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckNumericOnly_LongNumericString_ReturnsTrue()
    {
        var encoder = new TestBarcodeEncoder();
        // Longer than 18 chars to exercise the chunking logic
        var result = encoder.TestCheckNumericOnly("123456789012345678901234567890");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CheckNumericOnly_LongMixedString_ReturnsFalse()
    {
        var encoder = new TestBarcodeEncoder();
        var result = encoder.TestCheckNumericOnly("12345678901234567890ABC");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckNumericOnly_SingleDigit_ReturnsTrue()
    {
        var encoder = new TestBarcodeEncoder();
        var result = encoder.TestCheckNumericOnly("0");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CheckNumericOnly_EmptyString_ReturnsTrue()
    {
        var encoder = new TestBarcodeEncoder();
        // Empty string: TryParse fails but chunking produces empty array,
        // and LINQ All() returns true for empty sequences
        var result = encoder.TestCheckNumericOnly("");

        await Assert.That(result).IsTrue();
    }

    // Test subclass to expose the protected method
    private class TestBarcodeEncoder : BarcodeEncoder
    {
        public override string Encode(string data) => throw new NotImplementedException();
        public bool TestCheckNumericOnly(string data) => CheckNumericOnly(data);
    }
}
