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
    ''' The ACKNOWLEDGMENT RECEIPT on half letter (5.5" x 8.5"). One Render routine draws in points, so the on-screen
    ''' preview and the printed page are the same drawing. The system is not BIR-registered, so the footer says so.
    ''' </summary>
    Public NotInheritable Class InvoicePrinter

        Private Sub New()
        End Sub

        Public Const PageWidthPt As Single = 396.0F     ' 5.5 in
        Public Const PageHeightPt As Single = 612.0F    ' 8.5 in
        Private Const Side As Single = 28.0F
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        ' ================================================================ preview
        ''' <summary>The receipt as a picture <paramref name="widthPx"/> wide (height follows the paper's shape).</summary>
        Public Shared Function RenderPreview(r As Receipt, widthPx As Integer) As Bitmap
            Dim bmp = NewPageBitmap(widthPx, PageWidthPt, PageHeightPt)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Theme.White)
                g.PageUnit = GraphicsUnit.Point
                Render(g, r)
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
                doc.DefaultPageSettings.PaperSize = HalfLetter(doc.PrinterSettings)
                doc.DefaultPageSettings.Landscape = False
                doc.DefaultPageSettings.Margins = New Margins(0, 0, 0, 0)
                doc.PrintController = New StandardPrintController()   ' no system "Printing page 1" box
                AddHandler doc.PrintPage,
                    Sub(sender, e)
                        PreparePrinterGraphics(e)
                        Render(e.Graphics, r)
                        e.HasMorePages = False
                    End Sub
                doc.Print()
                Return doc.PrinterSettings.PrinterName
            End Using
        End Function

        ''' <summary>The printer's own 5.5" x 8.5" size (Statement) if it has one, else a custom half-letter size.</summary>
        Private Shared Function HalfLetter(settings As PrinterSettings) As PaperSize
            For Each size As PaperSize In settings.PaperSizes
                If size.Kind = PaperKind.Statement Then Return size
            Next
            Return New PaperSize("Half Letter", 550, 850)
        End Function

        ' ================================================================ drawing (points)
        Public Shared Sub Render(g As Graphics, r As Receipt)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
            Dim left = Side
            Dim right = PageWidthPt - Side
            Dim inner = right - left

            ' Brand motif: thin viewfinder brackets framing the page.
            Using pen As New Pen(Theme.Silver, 0.8F)
                Gfx.DrawViewfinder(g, New RectangleF(12, 12, PageWidthPt - 24, PageHeightPt - 24), 18, pen)
            End Using

            If r.PrintedCount > 0 Then
                Text(g, $"REPRINT · COPY {r.PrintedCount + 1}", Theme.Print.SmallBold, Theme.Slate, New RectangleF(left, 18, inner, 10), StringAlignment.Far)
            End If

            ' Studio header.
            Dim y = 26.0F
            Dim logo = LoadImage(r.LogoPath)
            If logo IsNot Nothing Then
                Using logo
                    Dim scale = Math.Min(110.0F / logo.Width, 40.0F / logo.Height)
                    Dim w = logo.Width * scale
                    Dim h = logo.Height * scale
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.DrawImage(logo, New RectangleF((PageWidthPt - w) / 2, y, w, h))
                    y += h + 6
                End Using
            End If
            y = Line(g, r.BusinessName, Theme.Print.Studio, Theme.Ink, left, inner, y, StringAlignment.Center)
            If r.Address.Length > 0 Then y = Line(g, r.Address, Theme.Print.Small, Theme.Slate, left, inner, y, StringAlignment.Center)
            If r.Contact.Length > 0 Then y = Line(g, r.Contact, Theme.Print.Small, Theme.Slate, left, inner, y, StringAlignment.Center)
            y += 9
            Tracked(g, "ACKNOWLEDGMENT RECEIPT", Theme.Print.DocTitle, Theme.Ink, PageWidthPt / 2, y, 1.6F)
            y += Theme.Print.DocTitle.GetHeight(g) + 6
            Rule(g, left, right, y, Theme.Ink, 0.8F)
            y += 8

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
            y += rowH * 3 + 8

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
            y += 6

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

            ' Voided: a large stamp across the page plus who/why.
            If r.IsVoided Then
                Dim state = g.Save()
                g.TranslateTransform(PageWidthPt / 2, PageHeightPt / 2)
                g.RotateTransform(-28)
                Using b As New SolidBrush(Color.FromArgb(70, Theme.Ink))
                    Using sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                        g.DrawString("VOID", Theme.Print.Stamp, b, New PointF(0, 0), sf)
                    End Using
                End Using
                g.Restore(state)
                Dim voidedOn = If(r.VoidedAt.HasValue, r.VoidedAt.Value.ToString("MMM d, yyyy h:mm tt", PH), "")
                Text(g, $"VOIDED {voidedOn}. Reason: {r.VoidReason}", Theme.Print.SmallBold, Theme.Ink, New RectangleF(left, y + 4, inner, 20))
            End If

            ' Footer.
            Dim fy = PageHeightPt - 30 - 52
            Rule(g, left, left + 130, fy + 14, Theme.Ink, 0.5F)
            Text(g, "Received by: " & r.CashierName, Theme.Print.Small, Theme.Slate, New RectangleF(left, fy + 16, 160, 9))
            fy += 32
            Line(g, $"Thank you for choosing {r.BusinessName}!", Theme.Print.Small, Theme.Slate, left, inner, fy, StringAlignment.Center)
            Line(g, "This document is not valid as a BIR invoice.", Theme.Print.SmallBold, Theme.Ink, left, inner, fy + 10, StringAlignment.Center)
        End Sub

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
