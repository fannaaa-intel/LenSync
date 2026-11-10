Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI.Dialogs

Namespace Modules.AddService

    ''' <summary>
    ''' Bill one booking: open the bill, and after the payment is saved show the receipt to print.
    ''' Shared by the Bookings ("Bill now") and Billing tabs.
    ''' </summary>
    Friend NotInheritable Class BillingFlow

        Private Sub New()
        End Sub

        ''' <summary>Returns True if a payment was saved.</summary>
        Public Shared Async Function RunAsync(owner As Control, bookingId As Integer) As Task(Of Boolean)
            Dim start As Tuple(Of Booking, List(Of PriceItem))
            owner.UseWaitCursor = True
            Try
                start = Await Task.Run(Function() Billing.StartBill(bookingId))
            Catch ex As Exception
                AppLog.Write(ex, "Opening a bill")
                LsDialog.Error(Db.FriendlyMessage(ex))
                Return False
            Finally
                owner.UseWaitCursor = False
            End Try
            Dim booking = start.Item1
            If booking Is Nothing Then
                LsDialog.Warning("This booking no longer exists. The list will refresh.", "Can't bill")
                Return False
            End If
            If booking.Status <> BookingStatus.Unpaid Then
                LsDialog.Warning($"{booking.BookingNo} is {booking.Status.ToLowerInvariant()}. Only shoots marked as done can be billed.", "Can't bill")
                Return False
            End If

            Dim outcome As SaleOutcome
            Using dlg As New BillingDialog(booking, start.Item2)
                If dlg.ShowCard() <> DialogResult.OK Then Return False
                outcome = dlg.Outcome
            End Using
            AppEvents.RaiseSalesChanged()

            Dim receipt As Receipt = Nothing
            Try
                receipt = Await Task.Run(Function() Billing.LoadReceipt(outcome.TransactionId))
            Catch ex As Exception
                AppLog.Write(ex, "Loading receipt after sale")
            End Try
            If receipt Is Nothing Then
                Toast.Show($"Payment saved as {outcome.ReceiptNo}. Open it from Sales to print the receipt.", ToastKind.Warning)
                Return True
            End If
            Using dlg As New ReceiptDialog(receipt, afterSale:=True, changeAmount:=outcome.ChangeAmount)
                dlg.ShowCard()
            End Using
            Return True
        End Function

    End Class

End Namespace
