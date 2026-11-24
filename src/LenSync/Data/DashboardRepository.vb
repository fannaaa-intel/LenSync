Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>Aggregate queries for the Dashboard. "Sales" = totals of Completed receipts by the day they were issued.</summary>
    Public NotInheritable Class DashboardRepository

        Private Sub New()
        End Sub

        ''' <summary>Completed sales per day from <paramref name="firstDay"/> through <paramref name="lastDay"/>; days without sales are 0.</summary>
        Public Shared Function SalesByDay(firstDay As Date, lastDay As Date) As List(Of SalesBucket)
            Const sql As String =
                "SELECT DATE(created_at) AS d, SUM(total) AS total, COUNT(*) AS receipts FROM transactions " &
                "WHERE status = 'Completed' AND created_at >= @from AND created_at < @to GROUP BY DATE(created_at)"
            Dim found = Db.Query(sql, Db.P("@from", firstDay.Date), Db.P("@to", lastDay.Date.AddDays(1))).Rows.Cast(Of DataRow)().
                ToDictionary(Function(r) Convert.ToDateTime(r("d")).Date,
                             Function(r) Tuple.Create(Convert.ToDecimal(r("total")), Convert.ToInt32(r("receipts"))))
            Dim days As New List(Of SalesBucket)
            Dim d = firstDay.Date
            While d <= lastDay.Date
                Dim hit As Tuple(Of Decimal, Integer) = Nothing
                Dim bucket As New SalesBucket With {.First = d, .Last = d}
                If found.TryGetValue(d, hit) Then
                    bucket.Total = hit.Item1
                    bucket.Receipts = hit.Item2
                End If
                days.Add(bucket)
                d = d.AddDays(1)
            End While
            Return days
        End Function

        ''' <summary>Finished shoots waiting for payment (bookings still Unpaid).</summary>
        Public Shared Function CountAwaitingPayment() As Integer
            Return Db.Scalar(Of Integer)("SELECT COUNT(*) FROM bookings WHERE is_active = 1 AND status = 'Unpaid'")
        End Function

    End Class

End Namespace
