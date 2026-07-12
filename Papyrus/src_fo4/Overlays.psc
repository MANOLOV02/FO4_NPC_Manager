Scriptname Overlays Native Hidden
; Native stub transcribed from LooksMenu / f4ee PapyrusOverlays.cpp RegisterFuncs()
; DECLARE_STRUCT(Entry, "Overlays") -> the struct below. Member names/types come from
; the overlay.Get(...)/overlay.Set(...) calls in Add/Set/Get/GetAll.

Struct Entry
	int uid
	int priority
	string template
	float red
	float green
	float blue
	float alpha
	float offset_u
	float offset_v
	float scale_u
	float scale_v
EndStruct

; NativeFunction3<StaticFunctionTag, UInt32, Actor*, bool, Entry>("Add", "Overlays")
int Function Add(Actor akActor, bool isFemale, Entry overlay) global native

; NativeFunction3<StaticFunctionTag, bool, Actor*, bool, UInt32>("Remove", "Overlays")
bool Function Remove(Actor akActor, bool isFemale, int uid) global native

; NativeFunction4<StaticFunctionTag, bool, Actor*, bool, UInt32, Entry>("Set", "Overlays")
bool Function Set(Actor akActor, bool isFemale, int uid, Entry overlay) global native

; NativeFunction3<StaticFunctionTag, Entry, Actor*, bool, UInt32>("Get", "Overlays")
Entry Function Get(Actor akActor, bool isFemale, int uid) global native

; NativeFunction2<StaticFunctionTag, bool, Actor*, bool>("RemoveAll", "Overlays")
bool Function RemoveAll(Actor akActor, bool isFemale) global native

; NativeFunction2<StaticFunctionTag, VMArray<Entry>, Actor*, bool>("GetAll", "Overlays")
Entry[] Function GetAll(Actor akActor, bool isFemale) global native

; NativeFunction1<StaticFunctionTag, void, Actor*>("Update", "Overlays")
Function Update(Actor akActor) global native

; NativeFunction0<StaticFunctionTag, void>("ClearAll", "Overlays")
Function ClearAll() global native
