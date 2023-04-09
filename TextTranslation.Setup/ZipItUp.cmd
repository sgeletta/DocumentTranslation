del bin\release\TextTranslation.zip
md temp
del temp\*.* /s /f /q

xcopy /q ..\TextTranslation.GUI\bin\Release\net6.0-windows\*.* temp
xcopy /q /y ..\TextTranslation.CLI\bin\Release\net6.0\*.* temp

tar.exe -a -c -p -f bin\release\TextTranslation.zip -C temp *.*
pause

del temp\*.* /s /f /q
rd temp