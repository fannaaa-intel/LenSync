Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>SQL for roles, permissions and role_permissions.</summary>
    Public NotInheritable Class RoleRepository

        Private Sub New()
        End Sub

        Public Shared Function ListRoles() As List(Of Role)
            Return Db.Query("SELECT id, name, IFNULL(description, '') AS description FROM roles ORDER BY id").Rows.Cast(Of DataRow)().
                Select(Function(r) New Role With {.Id = Db.Int(r, "id"), .Name = Db.Str(r, "name"), .Description = Db.Str(r, "description")}).
                ToList()
        End Function

        Public Shared Function ListPermissions() As List(Of Permission)
            Return Db.Query("SELECT id, perm_key, label FROM permissions ORDER BY id").Rows.Cast(Of DataRow)().
                Select(Function(r) New Permission With {.Id = Db.Int(r, "id"), .Key = Db.Str(r, "perm_key"), .Label = Db.Str(r, "label")}).
                ToList()
        End Function

        ''' <summary>Every (role id, permission id) grant.</summary>
        Public Shared Function ListGrants() As HashSet(Of Tuple(Of Integer, Integer))
            Dim t = Db.Query("SELECT role_id, permission_id FROM role_permissions")
            Return New HashSet(Of Tuple(Of Integer, Integer))(
                t.Rows.Cast(Of DataRow)().Select(Function(r) Tuple.Create(Db.Int(r, "role_id"), Db.Int(r, "permission_id"))))
        End Function

        ''' <summary>Replaces one role's permissions (inside the caller's transaction).</summary>
        Public Shared Sub ReplaceGrants(conn As MySqlConnection, tx As MySqlTransaction, roleId As Integer, permissionIds As IEnumerable(Of Integer))
            Using del = Db.Command(conn, "DELETE FROM role_permissions WHERE role_id = @r", tx, Db.P("@r", roleId))
                del.ExecuteNonQuery()
            End Using
            For Each pid In permissionIds
                Using ins = Db.Command(conn, "INSERT INTO role_permissions (role_id, permission_id) VALUES (@r, @p)", tx,
                                       Db.P("@r", roleId), Db.P("@p", pid))
                    ins.ExecuteNonQuery()
                End Using
            Next
        End Sub

    End Class

End Namespace
