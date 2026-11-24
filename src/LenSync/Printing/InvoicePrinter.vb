Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports System.Drawing.Text
Imports System.Globalization
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Dialogs
Imports LenSync.Printing.PrintKit

Namespace Printing

    ''' <summary>
    ''' The ACKNOWLEDGMENT RECEIPT: one receipt per page, 5.5" wide and only as tall as its content (the footer follows
    ''' the totals, so there is no empty space at the bottom; 12 lines still fit within half letter). One Render routine
    ''' draws in points, so the on-screen preview and the printed page are the same drawing. The system is not
    ''' BIR-registered, so the footer says so.
    ''' </summary>
    Public NotInheritable Class InvoicePrinter

        Private Sub New()
        End Sub

        Public Const PageWidthPt As Single = 396.0F     ' 5.5 in
        ''' <summary>The tallest a receipt gets (half letter, 8.5 in).</summary>
        Public Const MaxPageHeightPt As Single = 612.0F
        Private Const Side As Single = 26.0F
        Private Const BottomMargin As Single = 20.0F
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        ' ================================================================ size
        ''' <summary>The page height (points) this receipt needs: its content plus a small bottom margin.</summary>
        Public Shared Function PageHeightFor(r As Receipt) As Single
            Using bmp As New Bitmap(1, 1)
                Using g = Graphics.FromImage(bmp)
                    g.PageUnit = GraphicsUnit.Point
                    Dim bottom = Render(g, r, MaxPageHeightPt)
                    Return CSng(Math.Ceiling(Math.Min(MaxPageHeightPt, bottom + BottomMargin)))
                End Using
            End Using
        End Function

        ' ================================================================ preview
        ''' <summary>The receipt as a picture <paramref name="widthPx"/> wide (height follows the receipt's shape).</summary>
        Public Shared Function RenderPreview(r As Receipt, widthPx As Integer) As Bitmap
            Dim pageH = PageHeightFor(r)
            Dim bmp = NewPageBitmap(widthPx, PageWidthPt, pageH)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Theme.White)
                g.PageUnit = GraphicsUnit.Point
                Render(g, r, pageH)
            End Using
            Return bmp
        End Function

        ' ================================================================ print
        ''' <summary>
        ''' Lets the user pick the printer, then prints one copy. Returns the printer name, or Nothing if cancelled or
        ''' no printer is installed (the user has been told).
        ''' </summary>
        Public Shared Function Print(owner As IWin32Window, r As Receipt) As String
            If PrinterSettings.InstalledPrinters.Count = 0 Then
                LsDialog.Warning("No printer is installed on this computer. Add one in Windows Settings > Printers & scanners, then try again.",
                                 "No printer found")
                Return Nothing
            End If
            Using doc As New PrintDocument()
                doc.DocumentName = "Acknowledgment receipt " & r.ReceiptNo
                Using dlg As New PrintDialog() With {.Document = doc, .UseEXDialog = True, .AllowSomePages = False,
                                                     .AllowSelection = False, .AllowPrintToFile = False}
                    If dlg.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
                End Using
                ' One receipt per page: a 5.5" page exactly as tall as this receipt (hundredths of an inch).
                Dim pageH = PageHeightFor(r)
                doc.DefaultPageSettings.PaperSize = New PaperSize("LENSYNC receipt", 550, CInt(Math.Ceiling(pageH / 72.0F * 100.0F)))
                doc.DefaultPageSettings.Landscape = False
                doc.DefaultPageSettings.Margins = New Margins(0, 0, 0, 0)
                doc.PrintController = New StandardPrintController()   ' no system "Printing page 1" box
                AddHandler doc.PrintPage,
                    Sub(sender, e)
                        PreparePrinterGraphics(e)
                        Render(e.Graphics, r, pageH)
                        e.HasMorePages = False
                    End Sub
                doc.Print()
                Return doc.PrinterSettings.PrinterName
            End Using
        End Function

        ' ================================================================ drawing (points)
        ''' <summary>Draws the receipt on a page <paramref name="pageHeight"/> points tall; returns the bottom of the content.</summary>
        Public Shared Function Render(g As Graphics, r As Receipt, pageHeight As Single) As Single
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
            Dim left = Side
            Dim right = PageWidthPt - Side
            Dim inner = right - left

            ' Brand motif: thin viewfinder brackets framing the page.
            Using pen As New Pen(Theme.Silver, 0.8F)
                Gfx.DrawViewfinder(g, New RectangleF(10, 10, PageWidthPt - 20, pageHeight - 20), 16, pen)
            End Using

            If r.PrintedCount > 0 Then
                Text(g, $"REPRINT · COPY {r.PrintedCount + 1}", Theme.Print.SmallBold, Theme.Slate, New RectangleF(left, 14, inner, 10), StringAlignment.Far)
            End If

            ' Studio header.
            Dim y = 20.0F
            Dim logo = LoadImage(r.LogoPath)
            If logo IsNot Nothing Then
                Using logo
                    Dim scale = Math.Min(100.0F / logo.Width, 34.0F / logo.Height)
                    Dim w = logo.Width * scale
                    Dim h = logo.Height * scale
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.DrawImage(logo, New RectangleF((PageWidthPt - w) / 2, y, w, h))
                    y += h + 4
                End Using
            End If
            y = Line(g, r.BusinessName, Theme.Print.Studio, Theme.Ink, left, inner, y, StringAlignment.Center)
            If r.Address.Length > 0 Then y = Line(g, r.Address, Theme.Print.Small, Theme.Slate, left, inner, y, StringAlignment.Center)
            If r.Contact.Length > 0 Then y = Line(g, r.Contact, Theme.Print.Small, Theme.Slate, left, inner, y, StringAlignment.Center)
            y += 6
            Tracked(g, "ACKNOWLEDGMENT RECEIPT", Theme.Print.DocTitle, Theme.Ink, PageWidthPt / 2, y, 1.6F)
            y += Theme.Print.DocTitle.GetHeight(g) + 4
            Rule(g, left, right, y, Theme.Ink, 0.8F)
            y += 6

            ' Details: two columns of label + value.
            Dim colW = inner / 2 - 6
            Dim leftRows = {
                Tuple.Create("Receipt No.", r.ReceiptNo),
                Tuple.Create("Date", r.CreatedAt.ToString("MMM d, yyyy  h:mm tt", PH)),
                Tuple.Create("Cashier", r.CashierName)}
            Dim rightRows = {
                Tuple.Create("Customer", r.CustomerName),
                Tuple.Create("Contact", If(r.ContactNo.Length > 0, r.ContactNo, "—")),
                Tuple.Create("Booking", $"{r.BookingNo} · {r.ScheduleAt.ToString("MMM d, yyyy", PH)}")}
            Dim rowH = 12.5F
            For i = 0 To 2
                Pair(g, leftRows(i).Item1, leftRows(i).Item2, left, colW, y + i * rowH)
                Pair(g, rightRows(i).Item1, rightRows(i).Item2, left + inner / 2 + 6, colW, y + i * rowH)
            Next
            y += rowH * 3 + 6

            ' Items table.
            Dim qtyRight = right - 118
            Dim priceRight = right - 60
            Dim descW = qtyRight - 30 - left
            Text(g, "DESCRIPTION", Theme.Print.SmallBold, Theme.Slate, New RectangleF(left, y, descW, 10))
            Text(g, "QTY", Theme.Print.SmallBold, Theme.Slate, New RectangleF(qtyRight - 30, y, 30, 10), StringAlignment.Far)
            Text(g, "PRICE", Theme.Print.SmallBold, Theme.Slate, New RectangleF(priceRight - 56, y, 56, 10), StringAlignment.Far)
            Text(g, "AMOUNT", Theme.Print.SmallBold, Theme.Slate, New RectangleF(right - 58, y, 58, 10), StringAlignment.Far)
            y += 11
            Rule(g, left, right, y, Theme.Silver, 0.5F)
            y += 4
            Dim itemH = 13.0F
            For Each l In r.Lines
                Text(g, l.Description, Theme.Print.Body, Theme.Ink, New RectangleF(left, y, descW, itemH))
                Text(g, l.Qty.ToString(CultureInfo.InvariantCulture), Theme.Print.Body, Theme.Ink, New RectangleF(qtyRight - 30, y, 30, itemH), StringAlignment.Far)
                Text(g, Money.FormatPlain(l.UnitPrice), Theme.Print.Body, Theme.Ink, New RectangleF(priceRight - 56, y, 56, itemH), StringAlignment.Far)
                Text(g, Money.FormatPlain(l.LineTotal), Theme.Print.Body, Theme.Ink, New RectangleF(right - 58, y, 58, itemH), StringAlignment.Far)
                y += itemH
            Next
            y += 2
            Rule(g, left, right, y, Theme.Silver, 0.5F)
            y += 5

            ' Totals on the right half.
            Dim tx = left + inner * 0.38F
            Dim tw = right - tx
            y = Amount(g, "Subtotal", Money.Format(r.Subtotal), tx, tw, y, Theme.Print.Body)
            If r.DiscountAmount > 0D Then
                Dim label = If(r.DiscountType = DiscountTypes.Percent, $"Discount ({r.DiscountValue:0.##}%)", "Discount")
                y = Amount(g, label, "−" & Money.Format(r.DiscountAmount), tx, tw, y, Theme.Print.Body)
                If r.DiscountReason.Length > 0 Then
                    Text(g, "Reason: " & r.DiscountReason, Theme.Print.Small, Theme.Slate, New RectangleF(tx, y - 1, tw, 9))
                    y += 9
                End If
            End If
            If r.TaxAmount > 0D OrElse r.TaxRate > 0D Then
                y = Amount(g, $"Add: tax ({Billing.FormatRate(r.TaxRate)})", "+" & Money.Format(r.TaxAmount), tx, tw, y, Theme.Print.Body)
            End If
            y = Amount(g, "Total", Money.Format(r.Total), tx, tw, y, Theme.Print.BodyBold)
            If r.Downpayment > 0D Then
                Dim via = If(r.DownpaymentMethod.Length > 0, $" ({r.DownpaymentMethod}{If(r.DownpaymentRef.Length > 0, " " & r.DownpaymentRef, "")})", "")
                y = Amount(g, "Less: downpayment" & via, "−" & Money.Format(r.Downpayment), tx, tw, y, Theme.Print.Body)
            End If
            y += 2
            Rule(g, tx, right, y, Theme.Ink, 0.6F)
            y += 4
            Dim totalH = Theme.Print.Total.GetHeight(g)
            Text(g, "AMOUNT PAID", Theme.Print.SmallBold, Theme.Ink, New RectangleF(tx, y + (totalH - 9) / 2, tw / 2, 10))
            Text(g, Money.Format(r.BalanceDue), Theme.Print.Total, Theme.Ink, New RectangleF(tx, y, tw, totalH), StringAlignment.Far)
            y += totalH + 3
            Dim paidVia = r.PaymentMethod & If(r.ReferenceNo.Length > 0, " · Ref " & r.ReferenceNo, "")
            y = Amount(g, "Paid via", paidVia, tx, tw, y, Theme.Print.Body)
            If r.PaymentMethod = PaymentMethods.Cash Then
                y = Amount(g, "Cash received", Money.Format(r.AmountTendered), tx, tw, y, Theme.Print.Body)
                y = Amount(g, "Change", Money.Format(r.ChangeAmount), tx, tw, y, Theme.Print.Body)
            End If

            ' Voided (receipts voided before voiding moved to Billing): a large stamp across the page plus who/why.
            If r.IsVoided Then
                Dim state = g.Save()
                g.TranslateTransform(PageWidthPt / 2, pageHeight / 2)
                g.RotateTransform(-28)
                Using b As New SolidBrush(Color.FromArgb(70, Theme.Ink))
                    Using sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                        g.DrawString("VOID", Theme.Print.Stamp, b, New PointF(0, 0), sf)
                    End Using
                End Using
                g.Restore(state)
                Dim voidedOn = If(r.VoidedAt.HasValue, r.VoidedAt.Value.ToString("MMM d, yyyy h:mm tt", PH), "")
                Text(g, $"VOIDED {voidedOn}. Reason: {r.VoidReason}", Theme.Print.SmallBold, Theme.Ink, New RectangleF(left, y + 4, inner, 12))
                y += 16
            End If

            ' Footer, right under the totals (no gap down to a fixed page bottom).
            Dim fy = y + 10
            Rule(g, left, left + 130, fy + 14, Theme.Ink, 0.5F)
            Text(g, "Received by: " & r.CashierName, Theme.Print.Small, Theme.Slate, New RectangleF(left, fy + 16, 160, 9))
            fy += 30
            fy = Line(g, $"Thank you for choosing {r.BusinessName}!", Theme.Print.Small, Theme.Slate, left, inner, fy, StringAlignment.Center)
            fy = Line(g, "This document is not valid as a BIR invoice.", Theme.Print.SmallBold, Theme.Ink, left, inner, fy + 1, StringAlignment.Center)
            Return fy
        End Function

        ' ---------------------------------------------------------------- receipt-specific helpers (shared ones: PrintKit)
        ''' <summary>One line of text across the content width; returns the y below it.</summary>
        Private Shared Function Line(g As Graphics, s As String, font As Font, color As Color, left As Single, width As Single,
                                     y As Single, align As StringAlignment) As Single
            Dim h = font.GetHeight(g)
            Text(g, s, font, color, New RectangleF(left, y, width, h), align)
            Return y + h
        End Function

        Private Shared Sub Pair(g As Graphics, label As String, value As String, x As Single, width As Single, y As Single)
            Text(g, label, Theme.Print.Small, Theme.Slate, New RectangleF(x, y + 1, 50, 10))
            Text(g, value, Theme.Print.BodyBold, Theme.Ink, New RectangleF(x + 50, y, width - 50, 12))
        End Sub

        Private Shared Function Amount(g As Graphics, label As String, value As String, x As Single, width As Single, y As Single, font As Font) As Single
            Dim h = font.GetHeight(g) + 1.5F
            Dim valueW = g.MeasureString(value, font).Width + 4
            Text(g, label, Theme.Print.Body, Theme.Slate, New RectangleF(x, y, Math.Max(10, width - valueW - 4), h))
            Text(g, value, font, Theme.Ink, New RectangleF(x, y, width, h), StringAlignment.Far)
            Return y + h
        End Function

    End Class

End Namespace
