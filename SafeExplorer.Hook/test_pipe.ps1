$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "SafeExplorerPipe", [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(1000)
$writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
$writer.AutoFlush = $true
$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)

$json = '{"SourcePath":"C:\\test1","DestinationPath":"C:\\test2","Operation":1}'
$writer.WriteLine($json)
$resp = $reader.ReadLine()
Write-Host "Pipe Response: $resp"
