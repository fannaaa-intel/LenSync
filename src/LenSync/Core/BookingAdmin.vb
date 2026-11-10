Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Text.RegularExpressions
Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>
    ''' Add Service > Bookings: create/edit bookings with their downpayment, mark a shoot done (Pending -> Unpaid) and
    ''' cancel. Every action re-checks ADD_SERVICE and writes the audit entry in the same DB transaction.
    ''' </summary>
    Public NotInheritable Class BookingAdmin

        Private Sub New()
        End Sub

        Public Const MaxNotes As Integer = 1000
        Private Shared ReadOnly ContactPattern As New Regex("^[0-9+()\- ]{7,20}$")
        Private Shared ReadOnly ReferencePattern As New Regex("^[A-Z0-9-]{6,30}$")
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        ' ================================================================ validation (also used by the dialogs)
        Public Shared Function CustomerNameProblem(name As String) As String
            name = If(name, "").Trim()
            If name.Length < 2 Then Return "Enter the customer's name."
            If name.Length > 120 Then Return "Use at most 120 characters."
            Return Nothing
        End Function

        Public Shared Function ContactProblem(contact As String) As String
            contact = If(contact, "").Trim()
            If contact.Length = 0 Then Return "Enter a contact number."
            If Not ContactPattern.IsMatch(contact) OrElse contact.Count(AddressOf Char.IsDigit) < 7 Then
                Return "Use a phone number, e.g. 0917 123 4567."
            End If
            Return Nothing
        End Function

        ''' <summary>E-wallet reference numbers: letters, digits and dashes (spaces are removed), 6-30 characters.</summary>
        Public Shared Function NormalizeReference(reference As String) As String
            Return If(reference, "").Replace(" ", "").Trim().ToUpperInvariant()
        End Function

        Public Shared Function ReferenceProblem(reference As String) As String
            Dim r = NormalizeReference(reference)
            If r.Length = 0 Then Return "Enter the reference number from the GCash/Maya receipt."
            If Not ReferencePattern.IsMatch(r) Then Return "Use 6-30 letters or numbers from the e-wallet receipt."
            Return Nothing
        End Function

        Public Shared Function ScheduleProblem(scheduleAt As DateTime) As String
            If scheduleAt.Date < DateTime.Today Then Return "Choose today or a later date."
            If scheduleAt.Date > DateTime.Today.AddYears(2) Then Return "Choose a date within the next two years."
            Return Nothing
        End Function

        ' ================================================================ queries
        Public Shared Function List(filter As BookingFilter) As List(Of Booking)
            Session.Demand(Permissions.AddService)
            Return BookingRepository.List(filter)
        End Function

        ''' <summary>Packages that can be booked (active ones).</summary>
        Public Shared Function ActivePackages() As List(Of PriceItem)
            Session.Demand(Permissions.AddService)
            Return PriceItemRepository.List(PriceItemKind.Package).Where(Function(p) p.IsActive).ToList()
        End Function

        ' ================================================================ save
        ''' <summary>
        ''' Creates (Id = 0) or updates a booking. <paramref name="markDone"/>: the shoot already happened (walk-in), so the
        ''' booking goes straight to Unpaid for billing. Value = the booking id.
        ''' </summary>
        Public Shared Function Save(b As Booking, markDone As Boolean) As AdminResult
            Session.Demand(Permissions.AddService)
            Normalize(b)
            Dim isNew = b.Id = 0
            Dim existing As Booking = Nothing
            If Not isNew Then
                existing = BookingRepository.GetById(b.Id)
                If existing Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
            End If

            Dim result As New AdminResult()
            Dim p = CustomerNameProblem(b.CustomerName) : If p IsNot Nothing Then result.AddError("name", p)
            p = ContactProblem(b.ContactNo) : If p IsNot Nothing Then result.AddError("contact", p)
            p = UserAdmin.EmailProblem(b.CustomerEmail) : If p IsNot Nothing Then result.AddError("email", p)

            Dim package As PriceItem = Nothing
            If Not b.PackageId.HasValue Then
                result.AddError("package", "Choose a package.")
            Else
                package = PriceItemRepository.GetById(PriceItemKind.Package, b.PackageId.Value)
                Dim keptOld = existing IsNot Nothing AndAlso Nullable.Equals(existing.PackageId, b.PackageId)
                If package Is Nothing Then
                    result.AddError("package", "That package no longer exists. Choose another.")
                ElseIf Not package.IsActive AndAlso Not keptOld Then
                    result.AddError("package", "That package is no longer offered. Choose another.")
                End If
            End If

            ' A booking keeps its date when edited later, even if that day has passed.
            If isNew OrElse existing.ScheduleAt <> b.ScheduleAt Then
                p = ScheduleProblem(b.ScheduleAt) : If p IsNot Nothing Then result.AddError("date", p)
            End If

            If b.Downpayment < 0D OrElse b.Downpayment > Money.MaxAmount Then
                result.AddError("downpayment", "Enter a valid amount.")
            ElseIf package IsNot Nothing AndAlso b.Downpayment > package.Price Then
                result.AddError("downpayment", $"Can't exceed the package price ({Money.Format(package.Price)}).")
            End If
            If b.Downpayment > 0D Then
                If Not PaymentMethods.All.Contains(b.DownpaymentMethod) Then
                    result.AddError("dpMethod", "Choose how the downpayment was paid.")
                ElseIf PaymentMethods.IsEWallet(b.DownpaymentMethod) Then
                    p = ReferenceProblem(b.DownpaymentRef) : If p IsNot Nothing Then result.AddError("dpRef", p)
                End If
            End If
            If b.Notes.Length > MaxNotes Then result.AddError("notes", $"Use at most {MaxNotes} characters.")
            If Not result.Ok Then Return result

            If isNew Then
                Dim newId = Db.InTransaction(Of Integer)(
                    Function(conn, tx)
                        Dim no = Db.NextDocumentNo(conn, tx, "BK", 4)
                        Dim id = BookingRepository.Insert(conn, tx, b, no, If(markDone, BookingStatus.Unpaid, BookingStatus.Pending), Session.CurrentUserId)
                        AuditLog.Write(conn, tx, AuditActions.BookingCreate, "bookings", id,
                                       $"{no} for {b.CustomerName}: {package.Name} on {FormatWhen(b.ScheduleAt)}" & DownpaymentText(b) &
                                       If(markDone, "; shoot done (walk-in)", ""))
                        Return id
                    End Function)
                Return AdminResult.Success(newId)
            End If

            Dim outcome = Db.InTransaction(Of AdminResult)(
                Function(conn, tx)
                    Dim current = BookingRepository.GetForUpdate(conn, tx, b.Id)
                    If current Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
                    If Not current.IsEditable Then Return AdminResult.Fail("", $"This booking is already {current.Status.ToLowerInvariant()} and can't be edited.")
                    Dim changes = Describe(current, b, package)
                    If changes.Count = 0 AndAlso Not (markDone AndAlso current.Status = BookingStatus.Pending) Then Return AdminResult.Success(b.Id)
                    BookingRepository.Update(conn, tx, b)
                    If markDone AndAlso current.Status = BookingStatus.Pending Then
                        BookingRepository.SetStatus(conn, tx, b.Id, BookingStatus.Unpaid)
                        changes.Add("shoot marked done")
                    End If
                    AuditLog.Write(conn, tx, AuditActions.BookingUpdate, "bookings", b.Id, $"Updated {current.BookingNo}: " & String.Join("; ", changes))
                    Return AdminResult.Success(b.Id)
                End Function)
            Return outcome
        End Function

        Private Shared Sub Normalize(b As Booking)
            b.CustomerName = If(b.CustomerName, "").Trim()
            b.ContactNo = If(b.ContactNo, "").Trim()
            b.CustomerEmail = If(b.CustomerEmail, "").Trim()
            b.Notes = If(b.Notes, "").Trim()
            b.ScheduleAt = New DateTime(b.ScheduleAt.Year, b.ScheduleAt.Month, b.ScheduleAt.Day, b.ScheduleAt.Hour, b.ScheduleAt.Minute, 0)
            If b.Downpayment <= 0D Then
                b.Downpayment = 0D
                b.DownpaymentMethod = ""
            End If
            b.DownpaymentMethod = If(b.DownpaymentMethod, "")
            b.DownpaymentRef = If(PaymentMethods.IsEWallet(b.DownpaymentMethod), NormalizeReference(b.DownpaymentRef), "")
        End Sub

        Private Shared Function Describe(before As Booking, after As Booking, package As PriceItem) As List(Of String)
            Dim changes As New List(Of String)
            If before.CustomerName <> after.CustomerName Then changes.Add($"customer '{before.CustomerName}' -> '{after.CustomerName}'")
            If before.ContactNo <> after.ContactNo Then changes.Add("contact changed")
            If before.CustomerEmail <> after.CustomerEmail Then changes.Add("email changed")
            If Not Nullable.Equals(before.PackageId, after.PackageId) Then changes.Add($"package '{before.PackageName}' -> '{package.Name}'")
            If before.ScheduleAt <> after.ScheduleAt Then changes.Add($"schedule {FormatWhen(before.ScheduleAt)} -> {FormatWhen(after.ScheduleAt)}")
            If before.Downpayment <> after.Downpayment OrElse before.DownpaymentMethod <> after.DownpaymentMethod OrElse
               before.DownpaymentRef <> after.DownpaymentRef Then
                changes.Add($"downpayment {Money.Format(before.Downpayment)} {before.DownpaymentMethod} -> {Money.Format(after.Downpayment)} {after.DownpaymentMethod}".TrimEnd() &
                            If(after.DownpaymentRef.Length > 0, $" (ref {after.DownpaymentRef})", ""))
            End If
            If before.Notes <> after.Notes Then changes.Add("notes changed")
            Return changes
        End Function

        Private Shared Function DownpaymentText(b As Booking) As String
            If b.Downpayment <= 0D Then Return "; no downpayment"
            Return $"; downpayment {Money.Format(b.Downpayment)} via {b.DownpaymentMethod}" & If(b.DownpaymentRef.Length > 0, $" (ref {b.DownpaymentRef})", "")
        End Function

        Friend Shared Function FormatWhen(d As DateTime) As String
            Return d.ToString("MMM d, yyyy h:mm tt", PH)
        End Function

        ' ================================================================ status changes
        ''' <summary>Pending -> Unpaid: the shoot is done and the booking can be billed.</summary>
        Public Shared Function MarkDone(id As Integer) As AdminResult
            Session.Demand(Permissions.AddService)
            Return Db.InTransaction(Of AdminResult)(
                Function(conn, tx)
                    Dim b = BookingRepository.GetForUpdate(conn, tx, id)
                    If b Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
                    If b.Status <> BookingStatus.Pending Then Return AdminResult.Fail("", $"This booking is already {b.Status.ToLowerInvariant()}.")
                    BookingRepository.SetStatus(conn, tx, id, BookingStatus.Unpaid)
                    AuditLog.Write(conn, tx, AuditActions.BookingDone, "bookings", id, $"{b.BookingNo} ({b.CustomerName}): shoot done, ready for billing")
                    Return AdminResult.Success(id)
                End Function)
        End Function

        Public Shared Function ReasonProblem(reason As String) As String
            reason = If(reason, "").Trim()
            If reason.Length < 3 Then Return "Enter a short reason."
            If reason.Length > 255 Then Return "Use at most 255 characters."
            Return Nothing
        End Function

        ''' <summary>Pending/Unpaid -> Cancelled. The reason is added to the booking notes and the audit log.</summary>
        Public Shared Function Cancel(id As Integer, reason As String) As AdminResult
            Session.Demand(Permissions.AddService)
            reason = If(reason, "").Trim()
            Dim p = ReasonProblem(reason)
            If p IsNot Nothing Then Return AdminResult.Fail("reason", p)
            Return Db.InTransaction(Of AdminResult)(
                Function(conn, tx)
                    Dim b = BookingRepository.GetForUpdate(conn, tx, id)
                    If b Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
                    If Not b.IsEditable Then Return AdminResult.Fail("", $"This booking is already {b.Status.ToLowerInvariant()}.")
                    BookingRepository.SetStatus(conn, tx, id, BookingStatus.Cancelled, "Cancelled: " & reason)
                    AuditLog.Write(conn, tx, AuditActions.BookingCancel, "bookings", id,
                                   $"{b.BookingNo} ({b.CustomerName}) cancelled. Reason: {reason}" &
                                   If(b.Downpayment > 0D, $"; downpayment on record {Money.Format(b.Downpayment)}", ""))
                    Return AdminResult.Success(id)
                End Function)
        End Function

    End Class

End Namespace
