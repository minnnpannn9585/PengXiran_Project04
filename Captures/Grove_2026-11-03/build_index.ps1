$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$captureRoot = $PSScriptRoot
$shots = @(Get-Content -LiteralPath (Join-Path $captureRoot 'shots.json') -Raw | ConvertFrom-Json)
$animalTitles = @{'LowPoly_Wolf_6'='岩上的狼';'LowPoly_Deer_Calf_11'='林中的小鹿';'Fox_1'='花间的狐狸';'Rabbit_Correct'='巧克力色小兔';'Rabbit_WrongColor'='琥珀色小兔';'Rabbit_Scream'='象牙白小兔'}
$formatTitles = @{'Landscape'='横版';'Portrait'='肖像';'Square'='方图';'Environment'='环境';'Banner'='宽幅'}
$featuredNames = @('02_Wolf_Portrait','06_Fawn_Landscape','12_Fox_Portrait','18_Rabbit_Chocolate_Square','21_Rabbit_Amber_Landscape','27_Rabbit_Ivory_Portrait')
$jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object {$_.MimeType -eq 'image/jpeg'} | Select-Object -First 1
$jpegQuality = [System.Drawing.Imaging.EncoderParameters]::new(1)
$jpegQuality.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]88)
$thumbQuality = [System.Drawing.Imaging.EncoderParameters]::new(1)
$thumbQuality.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]86)
$cols=5; $tileW=420; $tileH=330; $gap=18; $pad=28; $header=88; $rows=[int][Math]::Ceiling($shots.Count/$cols)
$sheetWidth=$pad*2+$cols*$tileW+($cols-1)*$gap
$sheetHeight=$header+$rows*$tileH+($rows-1)*$gap+$pad
$sheet = [System.Drawing.Bitmap]::new($sheetWidth,$sheetHeight)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([System.Drawing.Color]::FromArgb(16,25,20))
$graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$headingFont=[System.Drawing.Font]::new('Segoe UI',25,[System.Drawing.FontStyle]::Regular)
$nameFont=[System.Drawing.Font]::new('Segoe UI',12,[System.Drawing.FontStyle]::Regular)
$detailFont=[System.Drawing.Font]::new('Microsoft YaHei UI',10,[System.Drawing.FontStyle]::Regular)
$textBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(238,234,221))
$mutedBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(168,182,167))
$goldBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(216,194,147))
$tileBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(24,35,29))
$graphics.DrawString('GROVE ELF / SCENE STILLS', $headingFont, $textBrush, [single]$pad,[single]18)
$graphics.DrawString(($shots.Count.ToString()+' PNG  /  2026.11.03'),$detailFont,$goldBrush,[single]($sheetWidth-250),[single]35)
$indexShots=[System.Collections.Generic.List[object]]::new()
try {
  for($i=0;$i -lt $shots.Count;$i++) {
    $shot=$shots[$i]
    $fileName=$shot.name+'.png'
    $sourcePath=Join-Path (Join-Path $captureRoot 'Final') $fileName
    $source=[System.Drawing.Image]::FromFile($sourcePath)
    try {
      if($source.Width -ne $shot.w -or $source.Height -ne $shot.h) { throw ('Dimension mismatch: '+$fileName) }
      $ratio=[Math]::Min(620.0/$source.Width,620.0/$source.Height)
      $tw=[Math]::Max(1,[int][Math]::Round($source.Width*$ratio));$th=[Math]::Max(1,[int][Math]::Round($source.Height*$ratio))
      $thumb=[System.Drawing.Bitmap]::new($tw,$th)
      $tg=[System.Drawing.Graphics]::FromImage($thumb)
      try {
        $tg.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $tg.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $tg.DrawImage($source,0,0,$tw,$th)
        $memory=[System.IO.MemoryStream]::new()
        try {$thumb.Save($memory,$jpegCodec,$thumbQuality);$dataUri='data:image/jpeg;base64,'+[Convert]::ToBase64String($memory.ToArray())} finally {$memory.Dispose()}
      } finally {$tg.Dispose();$thumb.Dispose()}
      $x=$pad+($i%$cols)*($tileW+$gap);$y=$header+[int][Math]::Floor($i/$cols)*($tileH+$gap)
      $graphics.FillRectangle($tileBrush,$x,$y,$tileW,$tileH)
      $imageAreaH=272
      $fit=[Math]::Min(($tileW-12.0)/$source.Width,($imageAreaH-10.0)/$source.Height)
      $dw=[int][Math]::Round($source.Width*$fit);$dh=[int][Math]::Round($source.Height*$fit)
      $dx=$x+[int](($tileW-$dw)/2);$dy=$y+[int](($imageAreaH-$dh)/2)
      $graphics.DrawImage($source,$dx,$dy,$dw,$dh)
      $graphics.DrawString($shot.name,$nameFont,$textBrush,[single]($x+11),[single]($y+279))
      $graphics.DrawString(($shot.w.ToString()+' x '+$shot.h.ToString()+'  /  PNG'),$detailFont,$mutedBrush,[single]($x+11),[single]($y+303))
      $suffix=($shot.name -split '_')[-1]
      $title=$animalTitles[$shot.animal]+' · '+$formatTitles[$suffix]
      $indexShots.Add([ordered]@{name=$shot.name;file=$fileName;animal=$shot.animal;w=$shot.w;h=$shot.h;title=$title;featured=($featuredNames -contains $shot.name);thumbnail=$dataUri})
    } finally {$source.Dispose()}
  }
  $sheet.Save((Join-Path $captureRoot 'contact_sheet.jpg'),$jpegCodec,$jpegQuality)
  $embedded=ConvertTo-Json -InputObject @($indexShots.ToArray()) -Depth 5 -Compress
  $htmlPath=Join-Path $captureRoot 'index.html'
  $html=[System.IO.File]::ReadAllText($htmlPath)
  $html=[regex]::Replace($html,'(?s)(<script id="shot-data" type="application/json">).*?(</script>)',[System.Text.RegularExpressions.MatchEvaluator]{param($match) $match.Groups[1].Value+$embedded+$match.Groups[2].Value})
  [System.IO.File]::WriteAllText($htmlPath,$html,[System.Text.UTF8Encoding]::new($false))
  Write-Output ('Built gallery and contact sheet for '+$shots.Count+' files. All dimensions match manifest.')
} finally {
  $graphics.Dispose();$sheet.Dispose();$headingFont.Dispose();$nameFont.Dispose();$detailFont.Dispose();$textBrush.Dispose();$mutedBrush.Dispose();$goldBrush.Dispose();$tileBrush.Dispose();$jpegQuality.Dispose();$thumbQuality.Dispose()
}
