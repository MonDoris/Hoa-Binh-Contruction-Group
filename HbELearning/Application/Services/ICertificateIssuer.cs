// File: Application/Services/ICertificateIssuer.cs

namespace HbELearning.Application.Services;

public interface ICertificateIssuer
{
    Task<Certificate> IssueAsync(int enrollmentId, decimal finalScore);
}
