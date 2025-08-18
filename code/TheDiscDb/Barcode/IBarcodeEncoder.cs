namespace TheDiscDb.Web.Barcode;

public interface IBarcodeEncoder
{
    string Encode(string data);
}