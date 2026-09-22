# Vérifie que chaque patch Harmony de Qmod.dll référence des paramètres
# existants de la méthode cible (Harmony exige les noms exacts).
param([string]$QmodDll = "build-tmp\Qmod.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll'
$managed = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed'
$asms = @{}
foreach ($f in @('assembly_valheim.dll', 'assembly_utils.dll', 'Assembly-CSharp.dll', 'assembly_guiutils.dll', 'gui_framework.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.UI.dll', 'Unity.TextMeshPro.dll')) {
  $p = "$managed\$f"
  if (Test-Path $p) { $asms[$f] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($p) }
}
function Find-Type($name) {
  foreach ($a in $asms.Values) { $t = $a.MainModule.GetType($name); if ($t) { return $t } }
  return $null
}
$special = @('__instance', '__result', '__state', '__args', '__originalMethod', '__runOriginal', '__exception')
$q = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$($PWD.ProviderPath)\$QmodDll")
$errors = 0; $checked = 0
function Visit($t) {
  foreach ($n in $t.NestedTypes) {
    $hp = $n.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' } | Select-Object -First 1
    if ($hp -and $hp.ConstructorArguments.Count -ge 2) {
      $targetType = "$($hp.ConstructorArguments[0].Value)"
      $targetMethod = "$($hp.ConstructorArguments[1].Value)"
      $tt = Find-Type $targetType
      if (-not $tt) { Write-Output "MANQUANT type cible: $($n.FullName) -> $targetType::$targetMethod"; $script:errors++; continue }
      $cands = @($tt.Methods | Where-Object { $_.Name -eq $targetMethod })
      if ($cands.Count -eq 0) { Write-Output "MANQUANT méthode cible: $($n.FullName) -> $targetType::$targetMethod"; $script:errors++; continue }
      foreach ($pm in ($n.Methods | Where-Object { $_.Name -eq 'Prefix' -or $_.Name -eq 'Postfix' -or $_.Name -eq 'Finalizer' -or $_.Name -eq 'Transpiler' })) {
        foreach ($par in $pm.Parameters) {
          $pn = $par.Name
          if ($pn.StartsWith('___') -or $special -contains $pn) { continue }
          $ok = $false
          foreach ($c in $cands) { if (@($c.Parameters | Where-Object { $_.Name -eq $pn }).Count -gt 0) { $ok = $true; break } }
          $script:checked++
          if (-not $ok) { Write-Output "PARAM INCONNU: $($n.FullName)::$($pm.Name) ($pn) cible=$targetType::$targetMethod [$($cands.Count) overload(s)]"; $script:errors++ }
        }
        # __result sur méthode void ?
        if (@($pm.Parameters | Where-Object { $_.Name -eq '__result' }).Count -gt 0) {
          $allVoid = $true
          foreach ($c in $cands) { if ($c.ReturnType.FullName -ne 'System.Void') { $allVoid = $false } }
          if ($allVoid) { Write-Output "__result SUR VOID: $($n.FullName)::$($pm.Name) cible=$targetType::$targetMethod"; $script:errors++ }
        }
      }
    }
    Visit $n
  }
}
foreach ($t in $q.MainModule.Types) { Visit $t }
Write-Output "params vérifiés: $checked, erreurs: $errors"
exit $errors
