Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una head part (<c>HDPT</c>) que se está editando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y editarlo
''' es editar lo que se va a guardar. Es el gemelo de <see cref="ArmaDraft"/> / <see cref="ArmoDraft"/>,
''' y el porqué de cada decisión de forma está escrito allá — acá se apunta a la ley y no se repite.</para>
'''
''' <para>Dos formas, como los otros: <b>nuevo</b> (identificador provisional, byte alto <c>0xFF</c>,
''' para que un NPC pueda referenciarlo antes de guardar) y <b>edición/override</b> (el identificador ES
''' el real del record que se sobrescribe).</para>
'''
''' <para>⛔ <b>Lo que este borrador tiene y los de armadura no: se referencia a SÍ MISMO.</b> El array
''' <c>HNAM</c> (Extra Parts) apunta a otros HDPT, así que un borrador puede apuntar a otro borrador de
''' su misma clase. Medido sobre el corpus: <b>2.254 de los 2.546 HDPT de Fallout 4 (89 %, por FormID ganador — `Tools\censo_hdpt.py`)</b> declaran
''' al menos un extra.</para>
''' <para>⚠️ <b>CORRECCIÓN</b> (el comentario anterior decía «que hasta ahora no existía para ninguna
''' clase», citando el «no ARMO→ARMO edge exists» de <c>NpcOverrideSaver</c>): eso es cierto <b>de
''' ARMO</b>, no del grafo. HDPT es la <b>SEGUNDA</b> clase con arista a sí misma: la primera es
''' <b>LVLI</b>, cuyo <c>LVLO\Item</c> se declara SIN firma en el esquema
''' (<c>WbSchemaGen_FO4.vb:1731</c>, <c>Wb.Fid("Item")</c>) y que ya tiene su clausura resuelta con
''' visitados en <c>NpcOverrideSaver.vb:891-899</c> («Walk nested draft LVLI → draft LVLI references
''' (cycle-safe via the visited set)») — <b>ése es el molde a copiar</b>. Y no es la única:
''' <c>FLST</c> también se auto-referencia y además puede contener HDPT, así que la clausura tiene que
''' llevar visitados <b>cruzando clases</b>, no sólo dentro de la propia.</para>
'''
''' <para>⛔ <b><see cref="Record"/> se tipa <c>IHdpt</c>, pero el editor opera por <c>TryCast</c> a la
''' vista concreta de Fallout 4.</b> <c>IHdpt</c> es la INTERSECCIÓN de los dos juegos y no expone cinco
''' campos que FO4 sí tiene y el corpus usa: <c>MODC</c>, <c>MODF</c>, el <c>MODS</c> que en FO4 es un
''' material swap, el bit 5 de <c>DATA</c> (<i>Uses Body Texture</i>, 8 records) y las condiciones
''' (<c>CTDA</c>, 40 records). Un editor escrito sólo contra la interfaz compila, anda, y los pierde.</para></summary>
Public Class HdptDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_HDPT_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IHdpt

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>True = edita un record existente. False = uno nuevo.</summary>
    Public Property IsOverride As Boolean

    ''' <summary>Todavía no se escribió nunca.</summary>
    Public Property IsNew As Boolean = True

    ''' <summary>Ya se escribió antes y se volvió a editar.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Cualquiera de las dos obliga a (re)escribirlo al guardar.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    '==============================================================================================
    ' Creación — las cinco puertas, iguales a las de ArmaDraft
    '==============================================================================================

    ''' <summary>Una head part nueva, vacía.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As HdptDraft
        Dim r = Canon.CanonRecords.HdptNuevo(game)
        Borradores.ExigirRecord(r, "HDPT", $"el formato de {game} no declara ese record")
        Return New HdptDraft With {.Record = r, .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el editor
    ''' tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As HdptDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Hdpt(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "HDPT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New HdptDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla). ⛔ No se reconstruye
    ''' campo por campo: copiar el árbol trae TODO lo que el record tenía, incluidos los campos que la
    ''' app no modela y los que ningún editor muestra —acá, las condiciones y el bloque <c>MODT</c>—, y
    ''' enumerarlos a mano garantiza que alguno falte. El porqué largo está en <c>ArmoDraft.Clon</c>,
    ''' con el caso medido que lo pagó.
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio uno
    ''' o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As HdptDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        Return ClonDesdeCopia(d.Record, formIDNuevo)
    End Function

    ''' <summary>La cola COMÚN de todo clon: exigir la copia y darle identidad nueva. Gemela de la de
    ''' <c>ArmoDraft</c>; el porqué —incluido el defecto del clon que nacía <c>Deleted</c> por heredar
    ''' <c>RecordFlags</c>— está allá.</summary>
    Private Shared Function ClonDesdeCopia(copia As Canon.IHdpt, formIDNuevo As UInteger) As HdptDraft
        Borradores.ExigirRecord(copia, "HDPT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim d As New HdptDraft With {.Record = copia, .FormID = formIDNuevo,
                                     .IsOverride = False, .IsNew = True}
        Borradores.ReidentificarComoClon(d.Record, formIDNuevo)
        Return d
    End Function

    ''' <summary>Un record NUEVO a partir de un BORRADOR PROPIO que el usuario ya está editando. Gemela
    ''' de <c>ArmoDraft.ClonDeBorrador</c>: «copiar» tiene que copiar LO QUE EL USUARIO VE, no la versión
    ''' del archivo. El porqué —incluido por qué NO se le cambia la firma a <see cref="Clon"/>— está allá.</summary>
    Public Shared Function ClonDeBorrador(origen As HdptDraft, formIDNuevo As UInteger) As HdptDraft
        If origen Is Nothing OrElse origen.Record Is Nothing Then
            Throw New ArgumentException(
                "ClonDeBorrador necesita un borrador CON record: sin él no hay árbol que copiar y el " &
                "editor abriría vacío en vez de con la copia que el usuario pidió.", NameOf(origen))
        End If
        Return ClonDesdeCopia(origen.Record.Copia(), formIDNuevo)
    End Function

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As HdptDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. Su resultado se registra en producción (el snapshot de
        ' apertura que la reversión vuelve a meter en el mapa que consultan el render y el guardado).
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "HDPT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New HdptDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado. Se compara por
    ''' los bytes que produciría cada uno: comparar campo por campo obliga a acordarse de todos, y el que
    ''' se olvida es justo el que después aparece como "editado" sin que nadie lo haya tocado.</summary>
    Public Function ContentEquals(o As HdptDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
