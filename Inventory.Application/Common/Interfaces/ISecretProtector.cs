namespace Inventory.Application.Common.Interfaces;

public interface ISecretProtector
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
