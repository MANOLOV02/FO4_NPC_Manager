Scriptname NiOverride Hidden
; Native stub transcribed 1:1 from RaceMenu / skee64 PapyrusNiOverride.cpp RegisterFuncs()
; Type map: TESObjectREFR*=ObjectReference  TESObjectARMO*=Armor  TESObjectARMA*=ArmorAddon
;           TESObjectWEAP*=Weapon  BGSTextureSet*=TextureSet  TESForm*=Form
;           BSFixedString=String  UInt32/SInt32=Int  float=Float  bool=Bool
;           VMArray<T>/VMResultArray<T> = T[]

; ---------------- Overlay Data ----------------
Int Function GetNumBodyOverlays() global native
Int Function GetNumHandOverlays() global native
Int Function GetNumFeetOverlays() global native
Int Function GetNumFaceOverlays() global native

Int Function GetNumSpellBodyOverlays() global native
Int Function GetNumSpellHandOverlays() global native
Int Function GetNumSpellFeetOverlays() global native
Int Function GetNumSpellFaceOverlays() global native

; ---------------- Overlays ----------------
Function AddOverlays(ObjectReference ref) global native
Bool Function HasOverlays(ObjectReference ref) global native
Function RemoveOverlays(ObjectReference ref) global native
Function RevertOverlays(ObjectReference ref) global native
Function RevertOverlay(ObjectReference ref, String nodeName, Int armorMask, Int addonMask) global native
Function RevertHeadOverlays(ObjectReference ref) global native
Function RevertHeadOverlay(ObjectReference ref, String nodeName, Int partType, Int shaderType) global native

; ---------------- Armor Overrides ----------------
Bool Function HasOverride(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
Function AddOverrideFloat(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index, Float value, Bool persist) global native
Function AddOverrideInt(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index, Int value, Bool persist) global native
Function AddOverrideBool(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index, Bool value, Bool persist) global native
Function AddOverrideString(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index, String value, Bool persist) global native
Function AddOverrideTextureSet(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index, TextureSet value, Bool persist) global native
Function ApplyOverrides(ObjectReference ref) global native
Bool Function HasArmorAddonNode(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Bool debug) global native

Float Function GetOverrideFloat(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
Int Function GetOverrideInt(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
Bool Function GetOverrideBool(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
String Function GetOverrideString(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
TextureSet Function GetOverrideTextureSet(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native

Float Function GetPropertyFloat(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
Int Function GetPropertyInt(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
Bool Function GetPropertyBool(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native
String Function GetPropertyString(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native

; ---------------- Node Overrides ----------------
Bool Function HasNodeOverride(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
Function AddNodeOverrideFloat(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index, Float value, Bool persist) global native
Function AddNodeOverrideInt(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index, Int value, Bool persist) global native
Function AddNodeOverrideBool(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index, Bool value, Bool persist) global native
Function AddNodeOverrideString(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index, String value, Bool persist) global native
Function AddNodeOverrideTextureSet(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index, TextureSet value, Bool persist) global native
Function ApplyNodeOverrides(ObjectReference ref) global native

Float Function GetNodeOverrideFloat(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
Int Function GetNodeOverrideInt(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
Bool Function GetNodeOverrideBool(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
String Function GetNodeOverrideString(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
TextureSet Function GetNodeOverrideTextureSet(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native

Float Function GetNodePropertyFloat(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
Int Function GetNodePropertyInt(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
Bool Function GetNodePropertyBool(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native
String Function GetNodePropertyString(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native

; ---------------- Weapon Overrides ----------------
Bool Function HasWeaponOverride(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
Function AddWeaponOverrideFloat(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index, Float value, Bool persist) global native
Function AddWeaponOverrideInt(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index, Int value, Bool persist) global native
Function AddWeaponOverrideBool(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index, Bool value, Bool persist) global native
Function AddWeaponOverrideString(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index, String value, Bool persist) global native
Function AddWeaponOverrideTextureSet(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index, TextureSet value, Bool persist) global native
Function ApplyWeaponOverrides(ObjectReference ref) global native
Bool Function HasWeaponNode(ObjectReference ref, Bool isFemale, Weapon wep, String nodeName, Bool firstPerson) global native

Float Function GetWeaponOverrideFloat(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
Int Function GetWeaponOverrideInt(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
Bool Function GetWeaponOverrideBool(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
String Function GetWeaponOverrideString(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
TextureSet Function GetWeaponOverrideTextureSet(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native

Float Function GetWeaponPropertyFloat(ObjectReference ref, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
Int Function GetWeaponPropertyInt(ObjectReference ref, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
Bool Function GetWeaponPropertyBool(ObjectReference ref, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native
String Function GetWeaponPropertyString(ObjectReference ref, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native

; ---------------- Skin Overrides ----------------
Bool Function HasSkinOverride(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native
Function AddSkinOverrideFloat(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index, Float value, Bool persist) global native
Function AddSkinOverrideInt(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index, Int value, Bool persist) global native
Function AddSkinOverrideBool(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index, Bool value, Bool persist) global native
Function AddSkinOverrideString(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index, String value, Bool persist) global native
Function AddSkinOverrideTextureSet(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index, TextureSet value, Bool persist) global native
Function ApplySkinOverrides(ObjectReference ref) global native

Float Function GetSkinOverrideFloat(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native
Int Function GetSkinOverrideInt(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native
Bool Function GetSkinOverrideBool(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native
String Function GetSkinOverrideString(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native
TextureSet Function GetSkinOverrideTextureSet(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native

Float Function GetSkinPropertyFloat(ObjectReference ref, Bool firstPerson, Int slotMask, Int key, Int index) global native
Int Function GetSkinPropertyInt(ObjectReference ref, Bool firstPerson, Int slotMask, Int key, Int index) global native
Bool Function GetSkinPropertyBool(ObjectReference ref, Bool firstPerson, Int slotMask, Int key, Int index) global native
String Function GetSkinPropertyString(ObjectReference ref, Bool firstPerson, Int slotMask, Int key, Int index) global native

; ---------------- Remove functions ----------------
Function RemoveAllOverrides() global native
Function RemoveAllReferenceOverrides(ObjectReference ref) global native
Function RemoveAllArmorOverrides(ObjectReference ref, Bool isFemale, Armor armor) global native
Function RemoveAllArmorAddonOverrides(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon) global native
Function RemoveAllArmorAddonNodeOverrides(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName) global native
Function RemoveOverride(ObjectReference ref, Bool isFemale, Armor armor, ArmorAddon addon, String nodeName, Int key, Int index) global native

Function RemoveAllNodeOverrides() global native
Function RemoveAllReferenceNodeOverrides(ObjectReference ref) global native
Function RemoveAllNodeNameOverrides(ObjectReference ref, Bool isFemale, String nodeName) global native
Function RemoveNodeOverride(ObjectReference ref, Bool isFemale, String nodeName, Int key, Int index) global native

Function RemoveAllWeaponBasedOverrides() global native
Function RemoveAllReferenceWeaponOverrides(ObjectReference ref) global native
Function RemoveAllWeaponOverrides(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep) global native
Function RemoveAllWeaponNodeOverrides(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName) global native
Function RemoveWeaponOverride(ObjectReference ref, Bool isFemale, Bool firstPerson, Weapon wep, String nodeName, Int key, Int index) global native

Function RemoveAllSkinBasedOverrides() global native
Function RemoveAllReferenceSkinOverrides(ObjectReference ref) global native
Function RemoveAllSkinOverrides(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask) global native
Function RemoveSkinOverride(ObjectReference ref, Bool isFemale, Bool firstPerson, Int slotMask, Int key, Int index) global native

; ---------------- Body Morph Manipulation ----------------
Bool Function HasBodyMorph(ObjectReference ref, String morphName, String keyName) global native
Function SetBodyMorph(ObjectReference ref, String morphName, String keyName, Float value) global native
Float Function GetBodyMorph(ObjectReference ref, String morphName, String keyName) global native
Function ClearBodyMorph(ObjectReference ref, String morphName, String keyName) global native
Bool Function HasBodyMorphKey(ObjectReference ref, String keyName) global native
Function ClearBodyMorphKeys(ObjectReference ref, String keyName) global native
Bool Function HasBodyMorphName(ObjectReference ref, String morphName) global native
Function ClearBodyMorphNames(ObjectReference ref, String morphName) global native
Function ClearMorphs(ObjectReference ref) global native
Function UpdateModelWeight(ObjectReference ref) global native
String[] Function GetMorphNames(ObjectReference ref) global native
String[] Function GetMorphKeys(ObjectReference ref, String morphName) global native
ObjectReference[] Function GetMorphedReferences() global native
Function ForEachMorphedReference(String eventName, Form receiver) global native
String[] Function GetCachedMorphNames() global native

; ---------------- Unique Item manipulation ----------------
Int Function GetItemUniqueID(ObjectReference ref, Int weaponSlot, Int slotIndex, Bool makeUnique) global native
Int Function GetObjectUniqueID(ObjectReference ref, Bool makeUnique) global native
Form Function GetFormFromUniqueID(Int uniqueID) global native
Form Function GetOwnerOfUniqueID(Int uniqueID) global native

; ---------------- DyeManager V1 ----------------
Function SetItemDyeColor(Int uniqueID, Int maskIndex, Int color) global native
Int Function GetItemDyeColor(Int uniqueID, Int maskIndex) global native
Function ClearItemDyeColor(Int uniqueID, Int maskIndex) global native
Function UpdateItemDyeColor(ObjectReference ref, Int uniqueID) global native

; ---------------- DyeManager V2 ----------------
Function SetItemTextureLayerColor(Int uniqueID, Int textureIndex, Int layerIndex, Int color) global native
Int Function GetItemTextureLayerColor(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function ClearItemTextureLayerColor(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function SetItemTextureLayerType(Int uniqueID, Int textureIndex, Int layerIndex, Int layerType) global native
Int Function GetItemTextureLayerType(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function ClearItemTextureLayerType(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function SetItemTextureLayerTexture(Int uniqueID, Int textureIndex, Int layerIndex, String texture) global native
String Function GetItemTextureLayerTexture(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function ClearItemTextureLayerTexture(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function SetItemTextureLayerBlendMode(Int uniqueID, Int textureIndex, Int layerIndex, String blendMode) global native
String Function GetItemTextureLayerBlendMode(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function ClearItemTextureLayerBlendMode(Int uniqueID, Int textureIndex, Int layerIndex) global native
Function UpdateItemTextureLayers(ObjectReference ref, Int uniqueID) global native

Function EnableTintTextureCache() global native
Function ReleaseTintTextureCache() global native
Bool Function IsFormDye(Form akForm) global native
Int Function GetFormDyeColor(Form akForm) global native
Function RegisterFormDyeColor(Form akForm, Int color) global native
Function UnregisterFormDyeColor(Form akForm) global native

; ---------------- Node Transforms ----------------
Bool Function HasNodeTransformPosition(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Function AddNodeTransformPosition(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name, Float[] position) global native
Float[] Function GetNodeTransformPosition(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Bool Function RemoveNodeTransformPosition(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native

Bool Function HasNodeTransformScale(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Function AddNodeTransformScale(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name, Float scale) global native
Float Function GetNodeTransformScale(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Bool Function RemoveNodeTransformScale(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native

Bool Function HasNodeTransformRotation(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Function AddNodeTransformRotation(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name, Float[] rotation) global native
Float[] Function GetNodeTransformRotation(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name, Int type) global native
Bool Function RemoveNodeTransformRotation(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native

Bool Function HasNodeTransformScaleMode(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Function AddNodeTransformScaleMode(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name, Int scaleMode) global native
Int Function GetNodeTransformScaleMode(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native
Bool Function RemoveNodeTransformScaleMode(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String name) global native

Function UpdateAllReferenceTransforms(ObjectReference ref) global native
Function UpdateNodeTransform(ObjectReference ref, Bool firstPerson, Bool isFemale, String node) global native
Function RemoveAllReferenceTransforms(ObjectReference ref) global native
Function RemoveAllTransforms() global native
Float Function GetInverseTransform(Float[] position, Float[] rotation, Float scale) global native
Function SetNodeDestination(ObjectReference ref, Bool firstPerson, Bool isFemale, String node, String destination) global native
Bool Function RemoveNodeDestination(ObjectReference ref, Bool firstPerson, Bool isFemale, String node) global native
String Function GetNodeDestination(ObjectReference ref, Bool firstPerson, Bool isFemale, String node) global native
String[] Function GetNodeTransformNames(ObjectReference ref, Bool firstPerson, Bool isFemale) global native
String[] Function GetNodeTransformKeys(ObjectReference ref, Bool firstPerson, Bool isFemale, String node) global native

; ---------------- Extra Data ----------------
Bool Function GetBooleanExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
Int Function GetIntegerExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
Int[] Function GetIntegersExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
Float Function GetFloatExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
Float[] Function GetFloatsExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
String Function GetStringExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native
String[] Function GetStringsExtraData(ObjectReference ref, Bool firstPerson, String node, String name) global native

; ---------------- Mesh Manipulation (latent) ----------------
Bool Function AttachMesh(ObjectReference ref, Bool firstPerson, String nodeName, String path, Bool replace, String[] filter) global native
Bool Function DetachMesh(ObjectReference ref, Bool firstPerson, String nodeName) global native
