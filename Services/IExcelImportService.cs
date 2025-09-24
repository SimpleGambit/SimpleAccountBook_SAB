using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleAccountBook.Models;

namespace SimpleAccountBook.Services;

public interface IExcelImportService
{
    Task<IList<TransactionRecord>> LoadAsync(string filePath, string? password = null);
    Task<IList<TransactionRecord>> LoadAsync(ReadOnlyMemory<byte> data, string? fileNameHint = null, string? password = null);
}