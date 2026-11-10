Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.Printing
Imports LenSync.UI
Imports LenSync.UI.Dialogs

''' <summary>
''' Shows the acknowledgment receipt exactly as it will print (half letter), right after a payment or when opened
''' from Sales. "Print receipt" lets the user pick the printer; each printed copy is counted and audited.
''' </summary>
Public Class ReceiptDialog
    Inherits LsFormDialog

    Private ReadOnly _receipt As Receipt
    Private _preview As Bitmap

    Public Sub New(receipt As Receipt, afterSale As Boolean, changeAmount As Decimal)
        MyBase.New(If(afterSale, "Payment saved", "Receipt"),
                   HeadingFor(receipt, afterSale, changeAmount),
                   SubtitleFor(receipt, afterSale),
                   If(receipt.PrintedCount > 0, "Reprint receipt", "Print receipt"), If(afterSale, "Done", "Close"),
                   PreviewSize().Width, PreviewSize().Height)
        _receipt = receipt
        If afterSale Then StrongFrame = True
        AddHandler Body.Paint, AddressOf PaintPreview
    End Sub

    Private Shared Function HeadingFor(r As Receipt, afterSale As Boolean, change As Decimal) As String
        If Not afterSale Then Return r.ReceiptNo
        If r.PaymentMethod = PaymentMethods.Cash AndAlso change > 0D Then Return "Give change: " & Money.Format(change)
        Return "Payment complete"
    End Function

    Private Shared Function SubtitleFor(r As Receipt, afterSale As Boolean) As String
        If afterSale Then Return $"{r.ReceiptNo} is saved for {r.CustomerName}. Print the receipt for the customer."
        Dim printed = If(r.PrintedCount > 0, $"printed {r.PrintedCount}×", "not printed yet")
        Return $"{r.CustomerName}  ·  {Money.Format(r.Total)}  ·  {If(r.IsVoided, "VOIDED", "Completed")}  ·  {printed}"
    End Function

    ''' <summary>Preview size (96-dpi) that fits the screen: as tall as possible, paper-shaped.</summary>
    Private Shared Function PreviewSize() As Size
        Dim availH = Screen.FromPoint(Cursor.Position).WorkingArea.Height / Theme.DpiScale
        Dim h = CInt(Math.Max(340, Math.Min(640, availH - 260)))
        Dim w = CInt(Math.Ceiling(h * InvoicePrinter.PageWidthPt / InvoicePrinter.PageHeightPt))
        Return New Size(Math.Max(w, 400), h)
    End Function

    Private Sub PaintPreview(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        Dim h = Body.Height - 2
        Dim w = CInt(h * InvoicePrinter.PageWidthPt / InvoicePrinter.PageHeightPt)
        If _preview Is Nothing OrElse _preview.Width <> w Then
            _preview?.Dispose()
            Try
                _preview = InvoicePrinter.RenderPreview(_receipt, w)
            Catch ex As Exception
                AppLog.Write(ex, "Rendering receipt preview")
                TextRenderer.DrawText(g, "The preview couldn't be drawn. You can still print the receipt.", Theme.Body, Body.ClientRectangle, Theme.Slate,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End Try
        End If
        Dim x = (Body.Width - _preview.Width) \ 2
        ' Explicit pixel size: the bitmap carries a paper DPI, which DrawImageUnscaled would honour (and enlarge).
        g.DrawImage(_preview, New Rectangle(x, 1, _preview.Width, _preview.Height))
        Using p As New Pen(Theme.Mist)
            g.DrawRectangle(p, x - 1, 0, _preview.Width + 1, _preview.Height + 1)
        End Using
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Dim printer As String
        Try
            printer = InvoicePrinter.Print(Me, _receipt)
        Catch ex As Exception
            AppLog.Write(ex, "Printing receipt " & _receipt.ReceiptNo)
            LsDialog.Error("The receipt couldn't be printed. Check that the printer is on and connected, then try again.", "Printing failed")
            Return False
        End Try
        If printer Is Nothing Then Return False
        SetBusy(True, "Saving...")
        Dim id = _receipt.TransactionId
        Await Task.Run(Function() Billing.RecordPrint(id, printer))
        SetBusy(False)
        Toast.Show($"Receipt {_receipt.ReceiptNo} sent to {printer}.", ToastKind.Success)
        Return True
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _preview?.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
