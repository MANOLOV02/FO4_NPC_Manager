Imports System.Windows.Forms

''' <summary>EL CAMINO DE BAJA DE UN BORRADOR DE MSWP, en UN solo lugar y compartido por los dos editores que
''' lo pueden elegir (ARMO y ARMA).
'''
''' <para>⛔ <b>POR QUÉ EXISTE: el MSWP era la única clase de borrador SIN salida.</b> Las otras cuatro
''' —OTFT, ARMO, ARMA y las listas por nivel— tienen su camino: los atuendos y las armaduras por el
''' «Delete / Revert…» de su selector (<c>ArmoEditor_Form.OnDeleteDraftEntry</c> y su gemelo de ARMA), y las
''' listas por nivel porque el selector de atuendos las da de baja al cerrar cuando nadie las reclama
''' (<c>OutfitPicker_Form.PlanDeCierreDeListas</c>). El MSWP no tenía ninguno:
''' <c>MainForm.UnregisterMswpDraft</c> se llamaba SÓLO desde el Cancel del sub-editor recién abierto, o sea
''' que un swap aceptado y después arrepentido no se podía sacar.</para>
'''
''' <para>⛔ <b>Y lo que costaba no era un registro de más.</b> La fase 2g emite TODO borrador de MSWP sucio,
''' referenciado o no (<c>NpcOverrideSaver</c>: <c>For Each d In mswpByFid.Values : If d.IsDirty Then …</c>),
''' y un MSWP es un record COMPARTIDO: un override de un swap vanilla le cambia el material a <b>toda</b>
''' armadura del orden de carga que lo use, no sólo a la que se estaba editando. Sin camino de baja, la única
''' salida era reiniciar la aplicación —perdiendo el resto del trabajo— o abrir el .esp en xEdit.</para>
'''
''' <para>⛔ <b>La ley es la MISMA que la de ARMO/ARMA, y por eso se escribe una vez.</b> Un OVERRIDE se
''' REVIERTE (se baja el borrador y se marca el record para que la fase 2a no lo vuelva a preservar, así gana
''' el original); un borrador NUEVO se BORRA, y sólo si no lo referencia nadie —el censo es
''' <c>GetDraftReferrers</c>, la fuente única—; y un record YA GUARDADO se marca para quitar en el próximo
''' guardado. Lo que NO se copia de aquella es el caso «es el que estás editando»: un MSWP nunca es el record
''' abierto en el editor de ARMO/ARMA, es el VALOR de uno de sus campos.</para>
'''
''' <para>⚠️ El campo que apuntaba al borrador NO se toca acá. Para un OVERRIDE el FormID es REAL y sigue
''' resolviendo al record original, así que la referencia queda sana. Para un borrador NUEVO el censo bloquea
''' la baja mientras algo lo apunte, así que tampoco puede quedar colgada.</para></summary>
Friend Module BorradoDeMswp

    ''' <summary>Da de baja el MSWP de <paramref name="entry"/> según su clase. Devuelve True si se dio de
    ''' baja (el selector saca la fila) y False si no se hizo nada — cancelado, bloqueado por referencias, o
    ''' la fila no es un MSWP que este camino sepa bajar.</summary>
    Friend Function BorrarORevertir(owner As IWin32Window, mainForm As MainForm,
                                    entry As FormIdPickerEntry) As Boolean
        If entry Is Nothing OrElse mainForm Is Nothing Then Return False
        Dim fid = entry.FormID
        If fid = 0UI Then Return False

        Dim d = mainForm.TryGetMswpDraft(fid)
        If d IsNot Nothing Then
            If Not d.IsNew Then
                ' OVERRIDE → REVERTIR. Bajar el borrador NO alcanza: si ese override ya se guardó, la fase 2a
                ' vuelve a preservar el record del plugin destino salvo que esté en `RecordsToRemove`, así que
                ' el swap revertido se seguiría escribiendo. Con la marca, la 2a lo deja caer y gana el
                ' original — que es lo que el usuario está pidiendo.
                If MessageBox.Show(owner,
                                   $"Revert material swap '{d.Record.EditorID}' to the original record?" & vbCrLf &
                                   "Your edits to this swap will be discarded." & vbCrLf & vbCrLf &
                                   "A material swap is SHARED: this affects every armor in the load order that uses it.",
                                   "Revert material swap", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
                mainForm.UnregisterMswpDraft(fid)
                mainForm.MarkRecordForRemoval(fid)
                mainForm.RevertAppOverrideInMemory(fid)
                Return True
            End If

            ' NUEVO → BORRAR, y sólo si no lo apunta nadie. El censo es el mismo que decide para las otras
            ' cuatro clases: un borrador referenciado que se borra deja el campo apuntando a un 0xFF muerto.
            Dim referrers = mainForm.GetDraftReferrers(fid)
            If referrers.Count > 0 Then
                MessageBox.Show(owner,
                                "Can't delete — this material swap is still referenced by:" & vbCrLf & vbCrLf &
                                String.Join(vbCrLf, referrers),
                                "Delete material swap", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
            If MessageBox.Show(owner, $"Delete material swap draft '{d.Record.EditorID}'? This cannot be undone.",
                               "Delete material swap", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
            mainForm.UnregisterMswpDraft(fid)
            Return True
        End If

        ' Ya GUARDADO en el plugin: se marca para quitar en el próximo guardado. Un record NUEVO (EDID npcm_)
        ' se borra; un OVERRIDE se revierte y vuelve a ganar el original. Misma redacción que ARMO/ARMA para
        ' que el usuario lea siempre lo mismo.
        Dim esNuevo = entry.EditorID IsNot Nothing AndAlso entry.EditorID.StartsWith("npcm_", StringComparison.OrdinalIgnoreCase)
        Dim verbo = If(esNuevo, "Delete", "Revert")
        Dim detalle = If(esNuevo, "It will be removed from your plugin on the next Save.",
                                  "The override will be dropped on the next Save — the original record wins again.")
        Dim refs = mainForm.GetDraftReferrers(fid)
        Dim aviso = If(refs.Count > 0, vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf & String.Join(vbCrLf, refs), "")
        If MessageBox.Show(owner,
                           $"{verbo} saved material swap '{entry.DisplayName}'?" & vbCrLf & detalle & vbCrLf & vbCrLf &
                           "A material swap is SHARED: this affects every armor in the load order that uses it." & aviso,
                           $"{verbo} saved material swap", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
        mainForm.MarkRecordForRemoval(fid)
        mainForm.RevertAppOverrideInMemory(fid)
        Return True
    End Function

End Module
