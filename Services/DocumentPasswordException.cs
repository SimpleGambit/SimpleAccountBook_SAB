using System;

namespace SimpleAccountBook.Services;

public enum ImportedDocumentType
{
    Excel,
    Pdf
}

public sealed class DocumentPasswordException : Exception
{
    private DocumentPasswordException(string message, ImportedDocumentType documentType, bool hadPassword, Exception innerException)
        : base(message, innerException)
    {
        DocumentType = documentType;
        HadPassword = hadPassword;
    }

    public ImportedDocumentType DocumentType { get; }

    public bool HadPassword { get; }

    public bool IsInvalidPassword => HadPassword;

    public bool PromptAttempted { get; private set; }

    public static DocumentPasswordException ForExcel(bool hadPassword, Exception innerException)
    {
        var message = hadPassword
            ? "입력한 비밀번호가 맞지 않아 Excel 파일을 열 수 없습니다."
            : "비밀번호를 입력해야 Excel 파일을 열 수 있습니다.";
        return new DocumentPasswordException(message, ImportedDocumentType.Excel, hadPassword, innerException);
    }

    public static DocumentPasswordException ForPdf(bool hadPassword, Exception innerException)
    {
        var message = hadPassword
            ? "입력한 비밀번호가 맞지 않아 PDF 파일을 열 수 없습니다."
            : "비밀번호를 입력해야 PDF 파일을 열 수 있습니다.";
        return new DocumentPasswordException(message, ImportedDocumentType.Pdf, hadPassword, innerException);
    }
}