Scriptname BodyGen Native Hidden
; Native stub transcribed from LooksMenu / f4ee PapyrusBodyGen.cpp RegisterFuncs()
; Class registered is "BodyGen". BGSKeyword* -> Keyword.

Function SetMorph(Actor akActor, bool isFemale, string morph, Keyword akKeyword, float value) global native
float Function GetMorph(Actor akActor, bool isFemale, string morph, Keyword akKeyword) global native
Function RemoveMorphsByName(Actor akActor, bool isFemale, string morph) global native
Function RemoveMorphsByKeyword(Actor akActor, bool isFemale, Keyword akKeyword) global native
Function RemoveAllMorphs(Actor akActor, bool isFemale) global native
Keyword[] Function GetKeywords(Actor akActor, bool isFemale, string morph) global native
string[] Function GetMorphs(Actor akActor, bool isFemale) global native
Function RegenerateMorphs(Actor akActor, bool update) global native
Function UpdateMorphs(Actor akActor) global native
Function ClearAll() global native
bool Function SetSkinOverride(Actor akActor, string id) global native
bool Function RemoveSkinOverride(Actor akActor) global native
