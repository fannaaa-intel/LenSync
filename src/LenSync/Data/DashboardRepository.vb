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
        Public Shared Function SalesByDay(firstDay As Date, lastDay As Date) As List(Of DaySales)
            Const sql As String =
                "SELECT DATE(created_at) AS d, SUM(total) AS total, COUNT(*) AS receipts FROM transactions " &
                "WHERE status = 'Completed' AND created_at >= @from AND created_at < @to GROUP BY DATE(created_at)"
            Dim found = Db.Query(sql, Db.P("@from", firstDay.Date), Db.P("@to", lastDay.Date.AddDays(1))).Rows.Cast(Of DataRow)().
                ToDictionary(Function(r) Convert.ToDateTime(r("d")).Date,
                             Function(r) New DaySales With {.Total = Convert.ToDecimal(r("total")), .Receipts = Convert.ToInt32(r("receipts"))})
            Dim days As New List(Of DaySales)
            Dim d = firstDay.Date
            While d <= lastDay.Date
                Dim hit As DaySales = Nothing
                days.Add(If(found.TryGetValue(d, hit), New DaySales With {.Day = d, .Total = hit.Total, .Receipts = hit.Receipts},
                            New DaySales With {.Day = d}))
                d = d.AddDays(1)
            End While
            Return days
        End Function

        ''' <summary>Receipts issued on <paramref name="day"/> that are now voided.</summary>
        Public Shared Function VoidedOn(day As Date) As Integer
            Return Db.Scalar(Of Integer)("SELECT COUNT(*) FROM transactions WHERE status = 'Voided' AND created_at >= @from AND created_at < @to",
                                         Db.P("@from", day.Date), Db.P("@to", day.Date.AddDays(1)))
        End Function

        ''' <summary>Finished shoots waiting for payment.</summary>
        Public Shared Function CountToBill() As Integer
            Return Db.Scalar(Of Integer)("SELECT COUNT(*) FROM bookings WHERE is_active = 1 AND status = 'Unpaid'")
        End Function

    End Class

End Namespace
