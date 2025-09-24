using System;
using System.Linq;

namespace SimpleAccountBook.Services;

/// <summary>
/// 거래 타입 결정을 위한 헬퍼 클래스
/// </summary>
public static class TransactionTypeHelper
{
    /// <summary>
    /// 거래 타입을 결정합니다 (입금/출금)
    /// </summary>
    /// <param name="typeText">구분 컬럼의 텍스트</param>
    /// <param name="withdrawAmount">출금 금액 (있는 경우)</param>
    /// <param name="depositAmount">입금 금액 (있는 경우)</param>
    /// <returns>"입금" 또는 "출금"</returns>
    public static string DetermineType(string typeText, decimal withdrawAmount = 0, decimal depositAmount = 0, decimal netAmount = 0)
    {
        var trimmed = typeText?.Trim();
        var canonical = NormalizeTypeText(typeText);
        // 1. 명시적으로 "입금" 또는 "출금"이 있으면 그대로 사용
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            if (string.Equals(canonical, "입금", StringComparison.Ordinal) ||
                string.Equals(canonical, "출금", StringComparison.Ordinal))
            {
                return canonical;
            }

            if (trimmed!.Contains("입금", StringComparison.Ordinal) || trimmed.Contains("입금액", StringComparison.Ordinal))
                return "입금";
            if (trimmed.Contains("출금", StringComparison.Ordinal) || trimmed.Contains("출금액", StringComparison.Ordinal))
                return "출금";
        }
        
        // 2. 입금/출금 금액으로 판단
        if (depositAmount > 0 && withdrawAmount == 0)
            return "입금";
        if (withdrawAmount > 0 && depositAmount == 0)
            return "출금";

        // 3. 단일 금액 컬럼만 존재하는 경우 부호로 판단
        if (netAmount != 0)
            return netAmount > 0 ? "입금" : "출금";

        // 4. 기본값은 타입 텍스트 또는 "출금"
        if (string.IsNullOrWhiteSpace(trimmed))
            return "출금";

        if (!string.IsNullOrWhiteSpace(canonical))
            return canonical;

        return trimmed ?? "출금";
    }

    public static string NormalizeTypeText(string? typeText)
    {
        if (typeText is null)
            return string.Empty;

        var trimmed = typeText.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var sanitized = string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c)));

        if (string.Equals(sanitized, "입금", StringComparison.Ordinal))
            return "입금";
        if (string.Equals(sanitized, "출금", StringComparison.Ordinal))
            return "출금";

        return trimmed;
    }
}