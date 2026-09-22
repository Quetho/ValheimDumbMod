param([string]$Type, [string[]]$Methods)
Add-Type -Path 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll'
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll')
$t = $a.MainModule.GetType($Type)
foreach ($mname in $Methods) {
  $m = $t.Methods | Where-Object { $_.Name -eq $mname } | Select-Object -First 1
  if (-not $m) { Write-Output "== $Type::$mname INTROUVABLE =="; continue }
  if (-not $m.HasBody) { Write-Output "== $Type::$mname (sans corps) =="; continue }
  Write-Output "== $Type::$mname =="
  foreach ($i in $m.Body.Instructions) {
    $o = $i.Operand; $op = ''
    if ($o -is [Mono.Cecil.MethodReference]) { $op = $o.DeclaringType.Name + '::' + $o.Name }
    elseif ($o -is [Mono.Cecil.FieldReference]) { $op = $o.DeclaringType.Name + '::' + $o.Name }
    elseif ($o -is [Mono.Cecil.Cil.Instruction]) { $op = '-> IL_{0:X4}' -f $o.Offset }
    elseif ($o -is [string]) { $op = "'$o'" }
    elseif ($o -ne $null -and $o.GetType().IsPrimitive) { $op = "$o" }
    Write-Output ("IL_{0:X4}: {1} {2}" -f $i.Offset, $i.OpCode, $op)
  }
}
