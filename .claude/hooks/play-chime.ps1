Add-Type -AssemblyName presentationCore
$player = New-Object System.Windows.Media.MediaPlayer
$player.Open([uri]'file:///c:/Users/kushal.f.sharma/repos/chum/_workspace/audio/wash_done_chime.mp3')
$player.Play()
Start-Sleep -Seconds 9
