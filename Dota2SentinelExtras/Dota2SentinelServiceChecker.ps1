$dir = "C:\Dota2Sentinel\Logs"
$latest = Get-ChildItem -Path $dir | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$date = Get-Date;
if ($latest.LastWriteTime.AddMinutes(30) -lt $date) {
    Restart-Service "Dota2Sentinel";
}