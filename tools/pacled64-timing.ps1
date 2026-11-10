param([string]$Dll)
# Times PacLED64 writes: the batch call vs per-pin calls. Leaves all LEDs off.
$src = @"
using System; using System.Runtime.InteropServices;
public static class Pac {
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] public static extern int PacInitialize();
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] public static extern void PacShutdown();
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] public static extern int PacGetProductId(int id);
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool Pac64SetLEDIntensity(int id, int port, byte v);
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool Pac64SetLEDIntensities(int id, byte[] d);
  [DllImport(@"$Dll", CallingConvention=CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool Pac64SetLEDFadeTime(int id, byte f);
}
"@
Add-Type -TypeDefinition $src
$n = [Pac]::PacInitialize(); "devices: $n"
$sw = [Diagnostics.Stopwatch]::new()
function T([scriptblock]$b, [int]$reps = 5) { $t = @(); for ($i = 0; $i -lt $reps; $i++) { $sw.Restart(); & $b; $t += $sw.Elapsed.TotalMilliseconds }; ($t | Measure-Object -Average -Minimum -Maximum) }
for ($id = 0; $id -lt $n; $id++) {
  [void][Pac]::Pac64SetLEDFadeTime($id, 0)
  $on = [byte[]]::new(64); for ($k = 0; $k -lt 64; $k++) { $on[$k] = 255 }; $off = [byte[]]::new(64)
  $script:flip = $false
  $batch = T { $script:flip = -not $script:flip; [void][Pac]::Pac64SetLEDIntensities($id, $(if ($script:flip) { $on } else { $off })) }
  $one = T { [void][Pac]::Pac64SetLEDIntensity($id, 0, 255); [void][Pac]::Pac64SetLEDIntensity($id, 0, 0) } 10
  $p64 = T { for ($k = 0; $k -lt 64; $k++) { [void][Pac]::Pac64SetLEDIntensity($id, $k, 128) } } 3
  $p16 = T { for ($k = 0; $k -lt 16; $k++) { [void][Pac]::Pac64SetLEDIntensity($id, $k, 64) } } 5
  [void][Pac]::Pac64SetLEDIntensities($id, $off)
  "board index $id (PID {0:X4}):" -f [Pac]::PacGetProductId($id)
  "  batch 64 LEDs : avg {0:N1} ms (min {1:N1}, max {2:N1})" -f $batch.Average, $batch.Minimum, $batch.Maximum
  "  per-pin x2    : avg {0:N1} ms  => {1:N2} ms per call" -f $one.Average, ($one.Average / 2)
  "  per-pin x64   : avg {0:N1} ms" -f $p64.Average
  "  per-pin x16   : avg {0:N1} ms" -f $p16.Average
}
[Pac]::PacShutdown()
