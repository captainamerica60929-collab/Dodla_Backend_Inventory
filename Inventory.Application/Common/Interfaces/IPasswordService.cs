namespace Inventory.Application.Common.Interfaces;

public interface IPasswordService
{
    string Hash(string plaintext);
    bool Verify(string plaintext, string hash);
}
