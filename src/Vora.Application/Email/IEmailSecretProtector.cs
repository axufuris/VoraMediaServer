namespace Vora.Application.Email;

public interface IEmailSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
