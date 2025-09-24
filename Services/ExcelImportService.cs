using ClosedXML.Excel;
using ExcelDataReader;
using SimpleAccountBook.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace SimpleAccountBook.Services;

public class ExcelImportService : IExcelImportService
{
    private static readonly string[] DateHeaders = new[] { "거래일시" };
    private static readonly string[] DatePartHeaders = new[] { "거래일자" };
    private static readonly string[] TimeHeaders = new[] { "거래시간" };
    private static readonly string[] TypeHeaders = new[] { "구분" };
    private static readonly string[] AmountHeaders = new[] { "거래금액" };
    private static readonly string[] WithdrawAmountHeaders = new[]
       {
        "출금(원)",
        "출금금액(원)",
        "출금금액",
        "출금액",
        "출금",
        "출금(-)",
        "출금금액(-)"
    };
    private static readonly string[] DepositAmountHeaders = new[]
    {
        "입금(원)",
        "입금금액(원)",
        "입금금액",
        "입금액",
        "입금",
        "입금(+)",
        "입금금액(+)"
    };
    private static readonly string[] CategoryHeaders = new[] { "거래구분", "적요" };
    private static readonly string[] DescriptionHeaders = new[] { "내용" };
    private static readonly byte[] EncryptedPackageMarker = Encoding.ASCII.GetBytes("EncryptedPackage");
    private static readonly byte[] EncryptionInfoMarker = Encoding.ASCII.GetBytes("EncryptionInfo");
    private readonly record struct DateColumnInfo(int? CombinedColumn, int? DateColumn, int? TimeColumn)
    {
        public bool HasCombined => CombinedColumn.HasValue;
        public bool HasSplit => DateColumn.HasValue && TimeColumn.HasValue;
    }
    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        var normalized = category.Trim();

        if (string.Equals(normalized, "체크카드결제", StringComparison.OrdinalIgnoreCase))
        {
            return "체크카드";
        }

        return normalized;
    }
    public Task<IList<TransactionRecord>> LoadAsync(string filePath, string? password = null)
    {
        return Task.Run(() => LoadInternal(filePath, password));
    }

    public Task<IList<TransactionRecord>> LoadAsync(ReadOnlyMemory<byte> data, string? fileNameHint = null, string? password = null)
    {
        return Task.Run(() => LoadInternal(data, fileNameHint, password));
    }

    private IList<TransactionRecord> LoadInternal(string filePath, string? password)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("파일을 찾을 수 없습니다.", filePath);
        }

        return DetermineExtension(filePath) switch
        {
            ".xls" => LoadLegacyExcelFromFile(filePath, password),
            ".pdf" => LoadPdfFromFile(filePath, password),
            _ => LoadWithClosedXmlFromFile(filePath, password)
        };
    }

    private IList<TransactionRecord> LoadInternal(ReadOnlyMemory<byte> data, string? fileNameHint, string? password)
    {
        if (data.IsEmpty)
        {
            return new List<TransactionRecord>();
        }

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        return DetermineExtension(fileNameHint) switch
        {
            ".xls" => LoadLegacyExcel(stream, password),
            ".pdf" => LoadPdf(stream, password),
            _ => LoadWithClosedXml(stream, password)
        };
    }
    private static string DetermineExtension(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return ".xlsx";
        }

        var extension = Path.GetExtension(pathOrName);
        if (string.IsNullOrEmpty(extension))
        {
            return ".xlsx";
        }

        return extension.ToLowerInvariant();
    }

    private static bool TryGetDateTime(object? rawValue, string text, out DateTime value)
    {
        switch (rawValue)
        {
            case DateTime dateTime:
                value = dateTime;
                return true;
            case double oaDate:
                value = DateTime.FromOADate(oaDate);
                return true;
        }

        text = text.Trim();
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value) ||
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);
    }
    private static bool TryGetTimeSpan(object? rawValue, string text, out TimeSpan value)
    {
        switch (rawValue)
        {
            case TimeSpan timeSpan:
                value = timeSpan;
                return true;
            case DateTime dateTimeValue:
                value = dateTimeValue.TimeOfDay;
                return true;
            case double oaDate:
                value = DateTime.FromOADate(oaDate).TimeOfDay;
                return true;
        }

        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            value = default;
            return false;
        }

        if (TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out value) ||
            TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime) ||
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            value = dateTime.TimeOfDay;
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
        {
            if (text.Length == 4)
            {
                var hours = numeric / 100;
                var minutes = numeric % 100;
                if (hours is >= 0 and < 24 && minutes is >= 0 and < 60)
                {
                    value = new TimeSpan(hours, minutes, 0);
                    return true;
                }
            }

            if (text.Length == 6)
            {
                var hours = numeric / 10000;
                var minutes = (numeric / 100) % 100;
                var seconds = numeric % 100;
                if (hours is >= 0 and < 24 && minutes is >= 0 and < 60 && seconds is >= 0 and < 60)
                {
                    value = new TimeSpan(hours, minutes, seconds);
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetDateTimeFromParts(object? dateValue, string dateText, object? timeValue, string timeText, out DateTime value)
    {
        dateText ??= string.Empty;
        timeText ??= string.Empty;

        if (!TryGetDateTime(dateValue, dateText, out var datePart))
        {
            var combinedText = string.Join(" ", new[] { dateText, timeText }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            if (string.IsNullOrEmpty(combinedText))
            {
                value = default;
                return false;
            }

            return TryGetDateTime(null, combinedText, out value);
        }

        if (string.IsNullOrWhiteSpace(timeText) && timeValue is null or DBNull)
        {
            value = datePart;
            return true;
        }

        if (TryGetTimeSpan(timeValue, timeText, out var timePart))
        {
            value = datePart.Date.Add(timePart);
            return true;
        }

        var fallbackText = string.Join(" ", new[] { dateText, timeText }
            .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

        if (string.IsNullOrEmpty(fallbackText))
        {
            value = datePart;
            return true;
        }

        return TryGetDateTime(null, fallbackText, out value);
    }
    private static bool TryGetDecimal(object? rawValue, string text, out decimal value)
    {
        switch (rawValue)
        {
            case decimal decimalValue:
                value = decimalValue;
                return true;
            case double doubleValue:
                value = Convert.ToDecimal(doubleValue);
                return true;
            case float floatValue:
                value = Convert.ToDecimal(floatValue);
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
        }

        text = text.Replace(",", string.Empty).Replace("원", string.Empty).Trim();
        text = text.Replace("(", "-").Replace(")", string.Empty);
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value) ||
               decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetNonZeroDecimal(object? rawValue, string text, out decimal value)
    {
        if (!TryGetDecimal(rawValue, text, out value))
        {
            return false;
        }

        return value != 0;
    }

    private IList<TransactionRecord> LoadWithClosedXmlFromFile(string filePath, string? password)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return LoadWithClosedXml(stream, password);
    }

    private IList<TransactionRecord> LoadWithClosedXml(Stream stream, string? password)
    {
        var workingStream = EnsureSeekableStream(stream, out var ownsStream);
        bool encryptedPackageDetected = false;

        try
        {
            if (workingStream.CanSeek)
            {
                workingStream.Position = 0;
                encryptedPackageDetected = IsEncryptedExcelPackage(workingStream);
                workingStream.Position = 0;
            }

            if (encryptedPackageDetected)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw DocumentPasswordException.ForExcel(false, new FileFormatException("암호로 보호된 Excel 파일입니다."));
                }

                if (TryLoadEncryptedWorkbookWithClosedXml(workingStream, password, out var records))
                {
                    return records;
                }

                if (workingStream.CanSeek)
                {
                    workingStream.Position = 0;
                }
                return LoadLegacyExcel(workingStream, password);
            }

            if (workingStream.CanSeek)
            {
                workingStream.Position = 0;
            }

            using var workbook = CreateWorkbook(workingStream, password);
            var worksheet = workbook.Worksheets.First();
            return ParseWorksheet(worksheet);
        }
        catch (FileFormatException ex) when (encryptedPackageDetected)
        {
            throw DocumentPasswordException.ForExcel(password is not null, ex);
        }
        catch (Exception ex) when (IsClosedXmlPasswordException(ex))
        {
            throw DocumentPasswordException.ForExcel(password is not null, ex);
        }
        finally
        {
            if (ownsStream)
            {
                workingStream.Dispose();
            }
        }
    }

    private static Stream EnsureSeekableStream(Stream stream, out bool ownsStream)
    {
        if (stream.CanSeek)
        {
            ownsStream = false;
            return stream;
        }

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        ownsStream = true;
        return copy;
    }

    private static bool TryLoadEncryptedWorkbookWithClosedXml(Stream stream, string password, out IList<TransactionRecord> records)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        try
        {
            using var workbook = CreateWorkbook(stream, password);
            var worksheet = workbook.Worksheets.First();
            records = ParseWorksheet(worksheet);
            return true;
        }
        catch (NotSupportedException)
        {
        }
        catch (FileFormatException)
        {
        }
        catch (Exception ex) when (IsClosedXmlPasswordException(ex))
        {
        }
        records = new List<TransactionRecord>();
        return false;
    }
    private static bool IsEncryptedExcelPackage(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var originalPosition = stream.Position;
        var isEncrypted = false;

        try
        {
            stream.Position = 0;
            isEncrypted = EncryptedPackageEnvelope.IsEncryptedPackageEnvelope(stream);
        }
        catch (FileFormatException)
        {
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }
        catch (NotSupportedException)
        {
        }
        finally
        {
            stream.Position = originalPosition;
        }

        if (isEncrypted)
        {
            return true;
        }
        return ContainsEncryptedPackageMarker(stream);
    }

    private static bool ContainsEncryptedPackageMarker(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var originalPosition = stream.Position;

        try
        {
            if (!stream.CanRead)
            {
                return false;
            }

            var length = stream.Length;
            if (length <= 0)
            {
                return false;
            }

            var readLength = (int)Math.Min(length, 128 * 1024);
            stream.Seek(-readLength, SeekOrigin.End);

            var buffer = ArrayPool<byte>.Shared.Rent(readLength);
            try
            {
                var totalRead = 0;
                while (totalRead < readLength)
                {
                    var read = stream.Read(buffer, totalRead, readLength - totalRead);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead == 0)
                {
                    return false;
                }

                return ContainsMarker(buffer, totalRead, EncryptedPackageMarker) ||
                       ContainsMarker(buffer, totalRead, EncryptionInfoMarker);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
    private static bool ContainsMarker(byte[] buffer, int count, ReadOnlySpan<byte> marker)
    {
        var markerLength = marker.Length;

        if (markerLength == 0 || count < markerLength)
        {
            return false;
        }

        for (var i = 0; i <= count - markerLength; i++)
        {
            var matched = true;
            for (var j = 0; j < markerLength; j++)
            {
                if (buffer[i + j] != marker[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static IList<TransactionRecord> ParseWorksheet(IXLWorksheet worksheet)
    {
        var (headerMap, headerRowNumber) = FindHeaderRow(worksheet);

        var dateColumns = ResolveDateColumns(headerMap);
        bool hasTypeColumn = TryFindColumn(headerMap, TypeHeaders, out var typeColumn);
        bool hasAmountColumn = TryFindColumn(headerMap, AmountHeaders, out var amountColumn);
        bool hasWithdrawColumn = TryFindColumn(headerMap, WithdrawAmountHeaders, out var withdrawColumn);
        bool hasDepositColumn = TryFindColumn(headerMap, DepositAmountHeaders, out var depositColumn);

        if (!hasAmountColumn && !hasWithdrawColumn && !hasDepositColumn)
        {
            throw new InvalidDataException($"필수 컬럼을 찾을 수 없습니다: {string.Join(", ", AmountHeaders.Concat(WithdrawAmountHeaders).Concat(DepositAmountHeaders))}");
        }

        int categoryColumn = FindColumn(headerMap, CategoryHeaders);
        int descriptionColumn = FindColumn(headerMap, DescriptionHeaders);

        var records = new List<TransactionRecord>();
        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRowNumber))
        {
            if (!TryGetTransactionTime(row, dateColumns, out var transactionTime))
            {
                continue;
            }

            var type = hasTypeColumn ? row.Cell(typeColumn).GetString().Trim() : string.Empty;
            var withdrawCell = hasWithdrawColumn ? row.Cell(withdrawColumn) : null;
            var depositCell = hasDepositColumn ? row.Cell(depositColumn) : null;
            
            if (!TryReadAmount(hasAmountColumn ? row.Cell(amountColumn) : null,
                      withdrawCell,
                      depositCell,
                      out var amount))
            {
                continue;
            }

            var category = NormalizeCategory(row.Cell(categoryColumn).GetString().Trim());
            var description = row.Cell(descriptionColumn).GetString().Trim();

            decimal withdrawAmt = 0, depositAmt = 0;
            if (hasWithdrawColumn && withdrawCell != null)
            {
                TryGetDecimal(withdrawCell.Value, withdrawCell.GetString(), out withdrawAmt);
            }
            if (hasDepositColumn && depositCell != null)
            {
                TryGetDecimal(depositCell.Value, depositCell.GetString(), out depositAmt);
            }

            var transactionType = TransactionTypeHelper.DetermineType(type, withdrawAmt, depositAmt, amount);

            records.Add(new TransactionRecord
            {
                TransactionTime = transactionTime,
                TransactionType = transactionType,
                Amount = Math.Abs(amount),
                Category = category,
                Description = description
            });
        }

        return records
           .OrderBy(r => r.TransactionTime)
           .ToList();
    }
    private static bool TryGetTransactionTime(IXLRow row, DateColumnInfo dateColumns, out DateTime transactionTime)
    {
        if (dateColumns.HasCombined && dateColumns.CombinedColumn.HasValue)
        {
            var cell = row.Cell(dateColumns.CombinedColumn.Value);
            if (cell.IsEmpty())
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTime(cell.Value, cell.GetString(), out transactionTime);
        }

        if (dateColumns.HasSplit && dateColumns.DateColumn.HasValue && dateColumns.TimeColumn.HasValue)
        {
            var dateCell = row.Cell(dateColumns.DateColumn.Value);
            var timeCell = row.Cell(dateColumns.TimeColumn.Value);

            var dateText = dateCell.GetString();
            var timeText = timeCell.GetString();

            if (dateCell.IsEmpty() && string.IsNullOrWhiteSpace(dateText))
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTimeFromParts(dateCell.Value, dateText, timeCell.Value, timeText, out transactionTime);
        }

        transactionTime = default;
        return false;
    }
    private IList<TransactionRecord> LoadLegacyExcelFromFile(string filePath, string? password)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return LoadLegacyExcel(stream, password);
    }

    private IList<TransactionRecord> LoadLegacyExcel(Stream stream, string? password)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
        try
        {
            using var reader = CreateReader(stream, password);
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = false
                }
            });

            if (dataSet.Tables.Count == 0)
            {
                return new List<TransactionRecord>();
            }
            var table = dataSet.Tables[0];
            return ParseLegacyTable(table);
        }
        catch (Exception ex) when (IsExcelDataReaderPasswordException(ex))
        {
            throw DocumentPasswordException.ForExcel(password is not null, ex);
        }
    }

    private static IList<TransactionRecord> ParseLegacyTable(DataTable table)
    {
        if (table.Rows.Count == 0)
        {
            return new List<TransactionRecord>();
        }

        var (headerMap, headerRowIndex) = FindHeaderRow(table);

        var dateColumns = ResolveDateColumns(headerMap);
        bool hasTypeColumn = TryFindColumn(headerMap, TypeHeaders, out var typeColumn);
        bool hasAmountColumn = TryFindColumn(headerMap, AmountHeaders, out var amountColumn);
        bool hasWithdrawColumn = TryFindColumn(headerMap, WithdrawAmountHeaders, out var withdrawColumn);
        bool hasDepositColumn = TryFindColumn(headerMap, DepositAmountHeaders, out var depositColumn);

        if (!hasAmountColumn && !hasWithdrawColumn && !hasDepositColumn)
        {
            throw new InvalidDataException($"필수 컬럼을 찾을 수 없습니다: {string.Join(", ", AmountHeaders.Concat(WithdrawAmountHeaders).Concat(DepositAmountHeaders))}");
        }

        int categoryColumn = FindColumn(headerMap, CategoryHeaders);
        int descriptionColumn = FindColumn(headerMap, DescriptionHeaders);

        var records = new List<TransactionRecord>();
        for (int rowIndex = headerRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (!TryGetTransactionTime(row, dateColumns, out var transactionTime))
            {
                continue;
            }

            var type = hasTypeColumn ? Convert.ToString(row[typeColumn - 1])?.Trim() ?? string.Empty : string.Empty;
            if (!TryReadAmount(row, hasAmountColumn ? amountColumn : (int?)null,
           hasWithdrawColumn ? withdrawColumn : (int?)null,
           hasDepositColumn ? depositColumn : (int?)null,
           out var amount))
            {
                continue;
            }

            var category = NormalizeCategory(Convert.ToString(row[categoryColumn - 1])?.Trim() ?? string.Empty);
            var description = Convert.ToString(row[descriptionColumn - 1])?.Trim() ?? string.Empty;

            decimal withdrawAmt = 0, depositAmt = 0;
            if (hasWithdrawColumn)
            {
                var withdrawValue = row[withdrawColumn - 1];
                TryGetDecimal(withdrawValue, Convert.ToString(withdrawValue) ?? string.Empty, out withdrawAmt);
            }
            if (hasDepositColumn)
            {
                var depositValue = row[depositColumn - 1];
                TryGetDecimal(depositValue, Convert.ToString(depositValue) ?? string.Empty, out depositAmt);
            }

            var transactionType = TransactionTypeHelper.DetermineType(type, withdrawAmt, depositAmt, amount);

            records.Add(new TransactionRecord
            {
                TransactionTime = transactionTime,
                TransactionType = transactionType,
                Amount = Math.Abs(amount),
                Category = category,
                Description = description
            });
        }

        return records
            .OrderBy(r => r.TransactionTime)
            .ToList();
    }

    private static bool IsNullOrEmpty(object? value, string text)
    {
        if (value is DBNull)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(text);
    }
    private static bool TryGetTransactionTime(DataRow row, DateColumnInfo dateColumns, out DateTime transactionTime)
    {
        if (dateColumns.HasCombined && dateColumns.CombinedColumn.HasValue)
        {
            var value = row[dateColumns.CombinedColumn.Value - 1];
            var text = Convert.ToString(value) ?? string.Empty;
            if (IsNullOrEmpty(value, text))
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTime(value, text, out transactionTime);
        }

        if (dateColumns.HasSplit && dateColumns.DateColumn.HasValue && dateColumns.TimeColumn.HasValue)
        {
            var dateValue = row[dateColumns.DateColumn.Value - 1];
            var timeValue = row[dateColumns.TimeColumn.Value - 1];
            var dateText = Convert.ToString(dateValue) ?? string.Empty;
            var timeText = Convert.ToString(timeValue) ?? string.Empty;

            if (IsNullOrEmpty(dateValue, dateText))
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTimeFromParts(dateValue, dateText, timeValue, timeText, out transactionTime);
        }

        transactionTime = default;
        return false;
    }

    private static bool TryGetTransactionTimeFromText(DateColumnInfo dateColumns, string dateText, string timeText, out DateTime transactionTime)
    {
        dateText ??= string.Empty;
        timeText ??= string.Empty;

        if (dateColumns.HasCombined && dateColumns.CombinedColumn.HasValue)
        {
            if (string.IsNullOrWhiteSpace(dateText))
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTime(null, dateText, out transactionTime);
        }

        if (dateColumns.HasSplit && dateColumns.DateColumn.HasValue && dateColumns.TimeColumn.HasValue)
        {
            if (string.IsNullOrWhiteSpace(dateText))
            {
                transactionTime = default;
                return false;
            }

            return TryGetDateTimeFromParts(null, dateText, null, timeText, out transactionTime);
        }

        transactionTime = default;
        return false;
    }


    private static bool TryReadAmount(IXLCell? amountCell, IXLCell? withdrawCell, IXLCell? depositCell, out decimal amount)
    {
        amount = 0;

        if (amountCell != null && TryGetNonZeroDecimal(amountCell.Value, amountCell.GetString(), out amount))
        {
            return true;
        }

        if (withdrawCell != null && TryGetNonZeroDecimal(withdrawCell.Value, withdrawCell.GetString(), out amount))
        {
            return true;
        }

        if (depositCell != null && TryGetNonZeroDecimal(depositCell.Value, depositCell.GetString(), out amount))
        {
            return true;
        }

        return false;
    }
    private static (Dictionary<string, int> HeaderMap, int HeaderRowNumber) FindHeaderRow(IXLWorksheet worksheet)
    {
        foreach (var row in worksheet.RowsUsed())
        {
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lastCell = row.LastCellUsed();
            var lastColumn = lastCell?.Address.ColumnNumber ?? 0;

            for (var column = 1; column <= lastColumn; column++)
            {
                var header = NormalizeHeader(row.Cell(column).GetString());
                if (string.IsNullOrEmpty(header) || headerMap.ContainsKey(header))
                {
                    continue;
                }

                headerMap[header] = column;
            }

            if (ContainsAllRequiredHeaders(headerMap))
            {
                return (headerMap, row.RowNumber());
            }
        }

        throw new InvalidDataException("필수 컬럼을 포함한 헤더 행을 찾을 수 없습니다.");
    }

    private static (Dictionary<string, int> HeaderMap, int HeaderRowIndex) FindHeaderRow(DataTable table)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var row = table.Rows[rowIndex];

            for (int column = 0; column < table.Columns.Count; column++)
            {
                var header = NormalizeHeader(Convert.ToString(row[column]));
                if (string.IsNullOrEmpty(header) || headerMap.ContainsKey(header))
                {
                    continue;
                }

                headerMap[header] = column + 1;
            }

            if (ContainsAllRequiredHeaders(headerMap))
            {
                return (headerMap, rowIndex);
            }
        }

        throw new InvalidDataException("필수 컬럼을 포함한 헤더 행을 찾을 수 없습니다.");
    }
    private static bool TryReadAmount(DataRow row, int? amountColumn, int? withdrawColumn, int? depositColumn, out decimal amount)
    {
        amount = 0;

        if (amountColumn.HasValue)
        {
            var value = row[amountColumn.Value - 1];
            var text = Convert.ToString(value) ?? string.Empty;
            if (TryGetNonZeroDecimal(value, text, out amount))
            {
                return true;
            }
        }

        if (withdrawColumn.HasValue)
        {
            var value = row[withdrawColumn.Value - 1];
            var text = Convert.ToString(value) ?? string.Empty;
            if (TryGetNonZeroDecimal(value, text, out amount))
            {
                return true;
            }
        }

        if (depositColumn.HasValue)
        {
            var value = row[depositColumn.Value - 1];
            var text = Convert.ToString(value) ?? string.Empty;
            if (TryGetNonZeroDecimal(value, text, out amount))
            {
                return true;
            }
        }

        return false;
    }
    private static bool ContainsAllRequiredHeaders(Dictionary<string, int> headerMap)
    {
        if (!HasDateColumns(headerMap))
        {
            return false;
        }

        var hasAmount = HasCandidate(headerMap, AmountHeaders);
        var hasWithdraw = HasCandidate(headerMap, WithdrawAmountHeaders);
        var hasDeposit = HasCandidate(headerMap, DepositAmountHeaders);

        if (!hasAmount && !hasWithdraw && !hasDeposit)
        {
            return false;
        }

        var hasType = HasCandidate(headerMap, TypeHeaders);
        if (!hasType && !hasWithdraw && !hasDeposit)
        {
            return false;
        }

        if (!HasCandidate(headerMap, CategoryHeaders))
        {
            return false;
        }

        if (!HasCandidate(headerMap, DescriptionHeaders))
        {
            return false;
        }

        return true;
    }

    private static bool HasDateColumns(Dictionary<string, int> headerMap)
    {
        if (HasCandidate(headerMap, DateHeaders))
        {
            return true;
        }

        return HasCandidate(headerMap, DatePartHeaders) && HasCandidate(headerMap, TimeHeaders);
    }

    private static bool HasCandidate(Dictionary<string, int> headerMap, IEnumerable<string> candidates)
    {
        return TryFindColumn(headerMap, candidates, out _);
    }

    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(header.Length);
        foreach (var ch in header)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
    private static DateColumnInfo ResolveDateColumns(Dictionary<string, int> headers)
    {
        if (TryFindColumn(headers, DateHeaders, out var combinedColumn))
        {
            return new DateColumnInfo(combinedColumn, null, null);
        }

        if (TryFindColumn(headers, DatePartHeaders, out var dateColumn) &&
            TryFindColumn(headers, TimeHeaders, out var timeColumn))
        {
            return new DateColumnInfo(null, dateColumn, timeColumn);
        }

        throw new InvalidDataException($"필수 컬럼을 찾을 수 없습니다: {string.Join(", ", DateHeaders.Concat(DatePartHeaders).Concat(TimeHeaders))}");
    }
    private static bool TryFindColumn(Dictionary<string, int> headers, IEnumerable<string> candidates, out int column)
    {
        var normalizedCandidates = new List<string>();

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeHeader(candidate);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            normalizedCandidates.Add(normalized);

            if (headers.TryGetValue(candidate, out column))
            {
                return true;
            }
        }
        foreach (var normalized in normalizedCandidates)
        {
            int? matchedColumn = null;

            foreach (var header in headers)
            {
                if (!header.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (matchedColumn.HasValue)
                {
                    matchedColumn = null;
                    break;
                }

                matchedColumn = header.Value;
            }

            if (matchedColumn.HasValue)
            {
                column = matchedColumn.Value;
                return true;
            }
        }
        column = default;
        return false;
    }

    private static int FindColumn(Dictionary<string, int> headers, IEnumerable<string> candidates)
    {
        if (TryFindColumn(headers, candidates, out var column))
        {
            return column;
        }

        throw new InvalidDataException($"필수 컬럼을 찾을 수 없습니다: {string.Join(", ", candidates)}");
    }
    private IList<TransactionRecord> LoadPdfFromFile(string filePath, string? password)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return LoadPdf(stream, password);
    }

    private IList<TransactionRecord> LoadPdf(Stream stream, string? password)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        try
        {
            using var document = OpenPdf(stream, password);
            return ParsePdfLines(ExtractPdfLines(document));
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw DocumentPasswordException.ForPdf(password is not null, ex);
        }
    }
    private static List<string> ExtractPdfLines(PdfDocument document)
    {
        var lines = new List<string>();
        foreach (var page in document.GetPages())
        {
            using var reader = new StringReader(ContentOrderTextExtractor.GetText(page));
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }
    private IList<TransactionRecord> ParsePdfLines(IReadOnlyList<string> lines)
    {
        Dictionary<string, int>? headerMap = null;
        List<string>? headerColumns = null;
        DateColumnInfo? dateColumns = null;
        var columnsIndex = (date: -1, time: -1, type: -1, amount: -1, withdraw: -1, deposit: -1, category: -1, description: -1);
        var records = new List<TransactionRecord>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) { continue; }

            var columns = SplitPdfColumns(line);
            if (columns.Count == 0) { continue; }

            if (TryBuildHeader(columns, out var map, out var headers))
            {
                headerMap = map;
                headerColumns = headers;
                dateColumns = ResolveDateColumns(headerMap);
                columnsIndex.date = dateColumns.Value.HasCombined && dateColumns.Value.CombinedColumn.HasValue
                    ? dateColumns.Value.CombinedColumn.Value - 1
                    : dateColumns.Value.DateColumn.HasValue ? dateColumns.Value.DateColumn.Value - 1 : -1;
                columnsIndex.time = dateColumns.Value.TimeColumn.HasValue ? dateColumns.Value.TimeColumn.Value - 1 : -1;
                columnsIndex.type = TryFindColumn(headerMap, TypeHeaders, out var typeIdx) ? typeIdx - 1 : -1;
                columnsIndex.amount = TryFindColumn(headerMap, AmountHeaders, out var amountIdx) ? amountIdx - 1 : -1;
                columnsIndex.withdraw = TryFindColumn(headerMap, WithdrawAmountHeaders, out var withdrawIdx) ? withdrawIdx - 1 : -1;
                columnsIndex.deposit = TryFindColumn(headerMap, DepositAmountHeaders, out var depositIdx) ? depositIdx - 1 : -1;
                columnsIndex.category = FindColumn(headerMap, CategoryHeaders) - 1;
                columnsIndex.description = FindColumn(headerMap, DescriptionHeaders) - 1;
                continue;
            }

            if (headerMap is null || headerColumns is null || dateColumns is null) { continue; }

            if (columns.Count < headerColumns.Count)
            {
                if (records.Count > 0)
                {
                    var additional = string.Join(" ", columns).Trim();
                    if (!string.IsNullOrEmpty(additional))
                    {
                        var last = records[^1];
                        last.Description = string.Join(" ", new[] { last.Description, additional }
                            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                    }
                }

                continue;
            }

            var dateText = GetCell(columns, columnsIndex.date);
            var timeText = columnsIndex.time >= 0 ? GetCell(columns, columnsIndex.time) : string.Empty;
            if (!TryGetTransactionTimeFromText(dateColumns.Value, dateText, timeText, out var transactionTime)) { continue; }

            var type = GetCell(columns, columnsIndex.type).Trim();
            var amountText = columnsIndex.amount >= 0 ? GetCell(columns, columnsIndex.amount) : null;
            var withdrawText = columnsIndex.withdraw >= 0 ? GetCell(columns, columnsIndex.withdraw) : null;
            var depositText = columnsIndex.deposit >= 0 ? GetCell(columns, columnsIndex.deposit) : null;
            if (!TryReadAmountFromText(amountText, withdrawText, depositText, out var amount)) { continue; }

            var category = NormalizeCategory(GetCell(columns, columnsIndex.category).Trim());
            var description = GetCell(columns, columnsIndex.description).Trim();

            decimal withdrawAmount = 0;
            decimal depositAmount = 0;
            if (!string.IsNullOrWhiteSpace(withdrawText))
            {
                TryGetDecimal(null, withdrawText, out withdrawAmount);
            }

            if (!string.IsNullOrWhiteSpace(depositText))
            {
                TryGetDecimal(null, depositText, out depositAmount);
            }

            var transactionType = TransactionTypeHelper.DetermineType(type, withdrawAmount, depositAmount, amount);
            records.Add(new TransactionRecord
            {
                TransactionTime = transactionTime,
                TransactionType = transactionType,
                Amount = Math.Abs(amount),
                Category = category,
                Description = description
            });
        }

        return records
           .OrderBy(r => r.TransactionTime)
           .ToList();
    }

    private static bool TryBuildHeader(IReadOnlyList<string> columns, out Dictionary<string, int> headerMap, out List<string> headerColumns)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        headerColumns = new List<string>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            headerColumns.Add(columns[i]);
            var normalized = NormalizeHeader(columns[i]);
            if (!string.IsNullOrEmpty(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = i + 1;
            }
        }

        if (ContainsAllRequiredHeaders(map))
        {
            headerMap = map;
            return true;
        }

        headerMap = default!;
        headerColumns = default!;
        return false;
    }

    private static IReadOnlyList<string> SplitPdfColumns(string line)
    {
        return Regex.Split(line, "\\s{2,}")
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrEmpty(part))
            .ToList();
    }

    private static string GetCell(IReadOnlyList<string> columns, int index)
    {
        return index < 0 || index >= columns.Count ? string.Empty : columns[index];
    }

    private static bool TryReadAmountFromText(string? amountText, string? withdrawText, string? depositText, out decimal amount)
    {
        amount = 0;
        if (!string.IsNullOrWhiteSpace(amountText) && TryGetNonZeroDecimal(null, amountText, out amount))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(withdrawText) && TryGetNonZeroDecimal(null, withdrawText, out amount))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(depositText) && TryGetNonZeroDecimal(null, depositText, out amount))
        {
            return true;
        }

        return false;
    }
    private static XLWorkbook CreateWorkbook(Stream stream, string? password)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        if (string.IsNullOrEmpty(password))
        {
            return new XLWorkbook(stream);
        }

        var workbookType = typeof(XLWorkbook);
        var loadOptions = CreateClosedXmlLoadOptions(workbookType, password);
        if (loadOptions is null)
        {
            throw new NotSupportedException("Password protected workbooks are not supported by the installed ClosedXML version.");
        }

        var loadOptionsType = loadOptions.GetType();

        var ctorWithOptionsOnly = workbookType.GetConstructor(new[] { typeof(Stream), loadOptionsType });
        if (ctorWithOptionsOnly is not null)
        {
            return (XLWorkbook)ctorWithOptionsOnly.Invoke(new[] { stream, loadOptions });
        }

        foreach (var ctor in workbookType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != 3 ||
                parameters[0].ParameterType != typeof(Stream) ||
                parameters[2].ParameterType != loadOptionsType)
            {
                continue;
            }

            var eventTrackingValue = GetEventTrackingValue(parameters[1].ParameterType);
            if (eventTrackingValue is null)
            {
                continue;
            }

            return (XLWorkbook)ctor.Invoke(new[] { stream, eventTrackingValue, loadOptions });
        }

        throw new NotSupportedException("Password protected workbooks are not supported by the installed ClosedXML version.");
    }

    private static object? CreateClosedXmlLoadOptions(Type workbookType, string password)
    {
        var loadOptionsType = workbookType.Assembly.GetType("ClosedXML.Excel.LoadOptions");
        if (loadOptionsType is null)
        {
            return null;
        }

        var instance = Activator.CreateInstance(loadOptionsType);
        if (instance is null)
        {
            return null;
        }

        var passwordProperty = loadOptionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property => property.CanWrite &&
                                         property.PropertyType == typeof(string) &&
                                         property.Name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0);

        if (passwordProperty is null)
        {
            return null;
        }

        passwordProperty.SetValue(instance, password);
        return instance;
    }

    private static object? GetEventTrackingValue(Type eventTrackingType)
    {
        if (!eventTrackingType.IsEnum)
        {
            return null;
        }

        foreach (var name in new[] { "Enabled", "Disabled" })
        {
            if (Enum.IsDefined(eventTrackingType, name))
            {
                return Enum.Parse(eventTrackingType, name);
            }
        }

        var values = Enum.GetValues(eventTrackingType);
        return values.Length > 0 ? values.GetValue(0) : null;
    }

    private static IExcelDataReader CreateReader(Stream stream, string? password)
    {
        return password is null
            ? ExcelReaderFactory.CreateReader(stream)
            : ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { Password = password });
    }

    private static PdfDocument OpenPdf(Stream stream, string? password)
    {
        if (password is null)
        {
            return PdfDocument.Open(stream);
        }

        var options = CreateParsingOptions(password);
        if (options is null)
        {
            throw new NotSupportedException("현재 설치된 PdfPig 버전에서는 암호화된 PDF를 열 수 없습니다. PdfPig 패키지를 업데이트하거나 암호를 제거하고 다시 시도하세요.");
        }

        var openWithOptions = typeof(PdfDocument).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, nameof(PdfDocument.Open), StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       typeof(Stream).IsAssignableFrom(parameters[0].ParameterType) &&
                       parameters[1].ParameterType.Name.Contains("ParsingOptions", StringComparison.OrdinalIgnoreCase);
            });

        if (openWithOptions is null)
        {
            throw new NotSupportedException("PdfPig에서 암호화된 PDF를 열기 위한 API를 찾을 수 없습니다. PdfPig 패키지를 업데이트하고 다시 시도하세요.");
        }

        return (PdfDocument)openWithOptions.Invoke(null, new[] { stream, options })!;
    }

    private static object? CreateParsingOptions(string password)
    {
        var candidateTypeNames = new[]
        {
            "UglyToad.PdfPig.Parsing.ParsingOptions, UglyToad.PdfPig",
            "UglyToad.PdfPig.ParsingOptions, UglyToad.PdfPig"
        };

        foreach (var typeName in candidateTypeNames)
        {
            var type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is not { } instance)
            {
                continue;
            }

            var passwordProperty = type.GetProperty("Password", BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty);
            if (passwordProperty is null || !passwordProperty.CanWrite)
            {
                continue;
            }

            passwordProperty.SetValue(instance, password);
            return instance;
        }

        return null;
    }

    private static bool IsClosedXmlPasswordException(Exception ex) => ContainsPasswordMessage(ex);

    private static bool IsExcelDataReaderPasswordException(Exception ex) => ContainsPasswordMessage(ex);

    private static bool ContainsPasswordMessage(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (!string.IsNullOrEmpty(message))
            {
                if (message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("암호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

}