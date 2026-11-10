Option Strict On
Option Explicit On
Option Infer On

Imports System.Text.RegularExpressions
Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>
    ''' Settings > Users and Settings > Roles &amp; access. Every method re-checks the SETTINGS permission, validates on the
    ''' server side, writes in one transaction with its audit entry, and protects the studio from locking itself out
    ''' (no deactivating yourself or the last Admin, Admin role keeps every permission).
    ''' </summary>
    Public NotInheritable Class UserAdmin

        Private Sub New()
        End Sub

        Private Shared ReadOnly UsernamePattern As New Regex("^[A-Za-z0-9._-]{3,30}$")
        Private Shared ReadOnly EmailPattern As New Regex("^[^@\s]+@[^@\s]+\.[^@\s]+$")

        ' ================================================================ validation (also used by the dialogs)
        Public Shared Function UsernameProblem(username As String) As String
            username = If(username, "").Trim()
            If username.Length = 0 Then Return "Enter a username."
            If Not UsernamePattern.IsMatch(username) Then Return "3-30 letters, numbers, dots, dashes or underscores (no spaces)."
            Return Nothing
        End Function

        Public Shared Function FullNameProblem(fullName As String) As String
            fullName = If(fullName, "").Trim()
            If fullName.Length < 2 Then Return "Enter the person's full name."
            If fullName.Length > 120 Then Return "Use at most 120 characters."
            Return Nothing
        End Function

        ''' <summary>Email is optional, but needed for Forgot password.</summary>
        Public Shared Function EmailProblem(email As String) As String
            email = If(email, "").Trim()
            If email.Length = 0 Then Return Nothing
            If email.Length > 150 OrElse Not EmailPattern.IsMatch(email) Then Return "Enter a valid email address, e.g. name@gmail.com."
            Return Nothing
        End Function

        ' ================================================================ users
        Public Shared Function ListUsers() As List(Of UserSummary)
            Session.Demand(Permissions.Settings)
            Return UserRepository.ListSummaries()
        End Function

        Public Shared Function ListRoles() As List(Of Role)
            Session.Demand(Permissions.Settings)
            Return RoleRepository.ListRoles()
        End Function

        ''' <summary>Creates an active user with a temporary password (returned in Value; shown once, never stored in plain).</summary>
        Public Shared Function CreateUser(fullName As String, username As String, email As String, roleId As Integer) As AdminResult
            Session.Demand(Permissions.Settings)
            fullName = If(fullName, "").Trim()
            username = If(username, "").Trim()
            email = If(email, "").Trim()
            Dim result As New AdminResult()
            Dim p = FullNameProblem(fullName) : If p IsNot Nothing Then result.AddError("fullName", p)
            p = UsernameProblem(username)
            If p IsNot Nothing Then
                result.AddError("username", p)
            ElseIf UserRepository.UsernameExists(username) Then
                result.AddError("username", "That username is already taken.")
            End If
            p = EmailProblem(email) : If p IsNot Nothing Then result.AddError("email", p)
            Dim role = RoleRepository.ListRoles().FirstOrDefault(Function(r) r.Id = roleId)
            If role Is Nothing Then result.AddError("role", "Choose a role.")
            If Not result.Ok Then Return result

            Dim temp = Passwords.NewTemporaryPassword(username)
            Dim hash = Passwords.Hash(temp)
            Dim id = Db.InTransaction(Of Integer)(
                Function(conn, tx)
                    Dim newId = UserRepository.Insert(conn, tx, username, fullName, If(email.Length = 0, Nothing, email), roleId, hash)
                    AuditLog.Write(conn, tx, AuditActions.UserCreate, "users", newId, $"Created user '{username}' ({role.Name})")
                    Return newId
                End Function)
            Return AdminResult.Success(temp)
        End Function

        Public Shared Function UpdateUser(id As Integer, fullName As String, email As String, roleId As Integer) As AdminResult
            Session.Demand(Permissions.Settings)
            fullName = If(fullName, "").Trim()
            email = If(email, "").Trim()
            Dim existing = UserRepository.GetById(id)
            If existing Is Nothing Then Return AdminResult.Fail("", "This user no longer exists.")
            Dim roles = RoleRepository.ListRoles()
            Dim newRole = roles.FirstOrDefault(Function(r) r.Id = roleId)

            Dim result As New AdminResult()
            Dim p = FullNameProblem(fullName) : If p IsNot Nothing Then result.AddError("fullName", p)
            p = EmailProblem(email) : If p IsNot Nothing Then result.AddError("email", p)
            If newRole Is Nothing Then
                result.AddError("role", "Choose a role.")
            ElseIf roleId <> existing.RoleId Then
                If id = Session.CurrentUser.Id Then
                    result.AddError("role", "You can't change your own role.")
                ElseIf String.Equals(existing.RoleName, Role.AdminName, StringComparison.OrdinalIgnoreCase) AndAlso
                       existing.IsActive AndAlso UserRepository.CountOtherActiveAdmins(id) = 0 Then
                    result.AddError("role", "LENSYNC needs at least one active Admin.")
                End If
            End If
            If Not result.Ok Then Return result

            Dim changes As New List(Of String)
            If fullName <> existing.FullName Then changes.Add($"name '{existing.FullName}' -> '{fullName}'")
            If email <> If(existing.Email, "") Then changes.Add("email changed")
            If roleId <> existing.RoleId Then changes.Add($"role {existing.RoleName} -> {newRole.Name}")
            If changes.Count = 0 Then Return AdminResult.Success()

            Db.InTransaction(
                Sub(conn, tx)
                    UserRepository.UpdateProfile(conn, tx, id, fullName, If(email.Length = 0, Nothing, email), roleId)
                    AuditLog.Write(conn, tx, AuditActions.UserUpdate, "users", id, $"Updated '{existing.Username}': " & String.Join("; ", changes))
                End Sub)
            If id = Session.CurrentUser.Id Then
                Session.CurrentUser.FullName = fullName
                Session.CurrentUser.Email = If(email.Length = 0, Nothing, email)
            End If
            Return AdminResult.Success()
        End Function

        Public Shared Function SetActive(id As Integer, active As Boolean) As AdminResult
            Session.Demand(Permissions.Settings)
            Dim existing = UserRepository.GetById(id)
            If existing Is Nothing Then Return AdminResult.Fail("", "This user no longer exists.")
            If existing.IsActive = active Then Return AdminResult.Success()
            If Not active Then
                If id = Session.CurrentUser.Id Then Return AdminResult.Fail("", "You can't deactivate your own account while you're logged in.")
                If String.Equals(existing.RoleName, Role.AdminName, StringComparison.OrdinalIgnoreCase) AndAlso
                   UserRepository.CountOtherActiveAdmins(id) = 0 Then
                    Return AdminResult.Fail("", "LENSYNC needs at least one active Admin. Make someone else an Admin first.")
                End If
            End If
            Db.InTransaction(
                Sub(conn, tx)
                    UserRepository.SetActive(conn, tx, id, active)
                    AuditLog.Write(conn, tx, If(active, AuditActions.UserActivate, AuditActions.UserDeactivate), "users", id,
                                   $"{If(active, "Reactivated", "Deactivated")} '{existing.Username}'")
                End Sub)
            Return AdminResult.Success()
        End Function

        ''' <summary>Offline password reset: new temporary password (in Value), must be changed at next log-in, lock cleared.</summary>
        Public Shared Function ResetPassword(id As Integer) As AdminResult
            Session.Demand(Permissions.Settings)
            Dim existing = UserRepository.GetById(id)
            If existing Is Nothing Then Return AdminResult.Fail("", "This user no longer exists.")
            If id = Session.CurrentUser.Id Then Return AdminResult.Fail("", "To change your own password, use Change password in the sidebar.")
            If Not existing.IsActive Then Return AdminResult.Fail("", "Reactivate this account before resetting its password.")
            Dim temp = Passwords.NewTemporaryPassword(existing.Username)
            Dim hash = Passwords.Hash(temp)
            Db.InTransaction(
                Sub(conn, tx)
                    UserRepository.SetTemporaryPassword(conn, tx, id, hash)
                    AuditLog.Write(conn, tx, AuditActions.PasswordResetByAdmin, "users", id,
                                   $"Temporary password issued for '{existing.Username}'")
                End Sub)
            Return AdminResult.Success(temp)
        End Function

        ' ================================================================ roles & access
        Public Shared Function ListPermissions() As List(Of Permission)
            Session.Demand(Permissions.Settings)
            Return RoleRepository.ListPermissions()
        End Function

        Public Shared Function ListGrants() As HashSet(Of Tuple(Of Integer, Integer))
            Session.Demand(Permissions.Settings)
            Return RoleRepository.ListGrants()
        End Function

        ''' <summary>
        ''' Saves the permission matrix. <paramref name="grants"/> = desired (role id, permission id) pairs for the
        ''' non-Admin roles; the Admin role is never changed. Takes effect at each user's next log-in.
        ''' </summary>
        Public Shared Function SaveGrants(grants As HashSet(Of Tuple(Of Integer, Integer))) As AdminResult
            Session.Demand(Permissions.Settings)
            Dim roles = RoleRepository.ListRoles()
            Dim perms = RoleRepository.ListPermissions()
            Dim current = RoleRepository.ListGrants()
            Dim summaries As New List(Of Tuple(Of Role, List(Of Integer), String))
            For Each role In roles.Where(Function(r) Not r.IsAdmin)
                Dim r = role
                Dim want = perms.Where(Function(pm) grants.Contains(Tuple.Create(r.Id, pm.Id))).Select(Function(pm) pm.Id).ToList()
                Dim had = perms.Where(Function(pm) current.Contains(Tuple.Create(r.Id, pm.Id))).Select(Function(pm) pm.Id).ToList()
                Dim added = perms.Where(Function(pm) want.Contains(pm.Id) AndAlso Not had.Contains(pm.Id)).Select(Function(pm) "+" & pm.Key)
                Dim removed = perms.Where(Function(pm) had.Contains(pm.Id) AndAlso Not want.Contains(pm.Id)).Select(Function(pm) "-" & pm.Key)
                Dim diff = String.Join(", ", added.Concat(removed))
                If diff.Length > 0 Then summaries.Add(Tuple.Create(r, want, diff))
            Next
            If summaries.Count = 0 Then Return AdminResult.Success()
            Db.InTransaction(
                Sub(conn, tx)
                    For Each s In summaries
                        RoleRepository.ReplaceGrants(conn, tx, s.Item1.Id, s.Item2)
                        AuditLog.Write(conn, tx, AuditActions.RolePermissionsUpdate, "roles", s.Item1.Id, $"{s.Item1.Name}: {s.Item3}")
                    Next
                End Sub)
            Return AdminResult.Success()
        End Function

    End Class

End Namespace
