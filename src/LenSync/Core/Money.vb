Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization

Namespace Core

    ''' <summary>Philippine peso formatting and parsing. Money is always Decimal (never Double/Single).</summary>
    Public NotInheritable Class Money

        Private Sub New()
        End Sub

        Public Const MaxAmount As Decimal = 9999999.99D

        ''' <summary>₱1,500.00</summary>
        Public Shared Function Format(amount As Decimal) As String
            Return amount.ToString("₱#,##0.00", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>1,500.00 (for editing inside an input box).</summary>
        Public Shared Function FormatPlain(amount As Decimal) As String
            Return amount.ToString("#,##0.00", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' Accepts "1500", "1,500", "₱1,500.50", " 1500.5 ". Rejects negatives, more than 2 decimals and amounts above
        ''' MaxAmount. Returns False with a user-facing <paramref name="problem"/>.
        ''' </summary>
        Public Shared Function TryParse(text As String, ByRef amount As Decimal, ByRef problem As String) As Boolean
            amount = 0D
            problem = Nothing
            Dim s = If(text, "").Replace("₱", "").Replace(",", "").Replace("PHP", "").Replace("Php", "").Trim()
            If s.Length = 0 Then
                problem = "Enter an amount."
                Return False
            End If
            Dim value As Decimal
            If Not Decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, value) Then
                problem = "Use numbers only, e.g. 1500 or 1,500.00."
                Return False
            End If
            If Decimal.Round(value, 2) <> value Then
                problem = "Use at most 2 decimal places."
                Return False
            End If
            If value > MaxAmount Then
                problem = $"The amount can't be more than {Format(MaxAmount)}."
                Return False
            End If
            amount = value
            Return True
        End Function

    End Class

End Namespace
