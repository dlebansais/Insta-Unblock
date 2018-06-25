# Insta-Unblock
Automatically unblock files downloaded from the Internet.

# Using the program
Copy [the latest release](https://github.com/dlebansais/Insta-Unblock/releases/download/v1.0.57/InstaUnblock.exe) in a directory, then run it as administrator. This will create a little icon in the task bar.

Right-click the icon to pop a menu with the following items:

- Load at startup. When checked, the application is loaded when a user logs in.
- Unblock. All files that appear in the default download folder are immediately unblocked.
- Exit

# How does it work?
You can manually unblock a file by opening a command-line prompt and typing (for *myprogram.exe*) 
> echo.>myprogram.exe:Zone.Identifier.

You can also right-click it in the Explorer, choose Properties, check the **Unblock** box and confirm.
All this application is doing is watching for your personal download folder for new files, and unblocking them with the same command as above.
  
# Screenshots

![Menu](/Screenshots/Menu.png?raw=true "The app menu")

# Certification
This program is digitally signed with a [CAcert](https://www.cacert.org/) certificate.
