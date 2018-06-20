# ft8push
Quick lash-up to push spots from WSJT-X to iOS

## Instructions for use
* Register an account at pushover.net, set up Pushover on your mobile device
* Paste your pushover.net user key into `myPushoverUserKey`
* Set `myLocator` to your four-character locator
* Compile using Visual Studio 2017 Professional (.NET Core 2.0), run and leave
* Start WSJT-X, with File -> Settings -> Reporting -> UDP Server set to:
  * UDP Server 127.0.0.1
  * UDP Server port number 2237
  * all three checkboxes checked (maybe not necessary)
  
Bit of refinement would make this lovely.
