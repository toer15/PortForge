# PortForge 0.30 / Engine 1.13

The Controllers window reads the bundled engine's windowless SDL controller monitor. It offers Automatic, Xbox, DualSense, DualShock 4, Switch Pro, paired or individual Joy-Cons, and generic controller drawings. Buttons highlight, analog triggers fill, and stick caps follow their axes.

The shell and both Joy-Con halves use a fixed scale and translation. Gyroscope samples never enter the drawing transform. The motion/recenter UI was removed at the user's request. In-game motion input remains available through the existing engine path.

Saved layout settings apply through the game's supported engine keys, with backups and preservation of custom maps. Games already running are skipped. A chart translates Switch button names into the selected controller's labels. This does not replace game textures or embedded button glyphs.

Visual check: the rebuilt window detected the connected DualSense Edge and displayed its live R2 input on the fixed picture. Capture: verification/controller-window-0.30.png. Other controller families have drawings but have not been tested on physical hardware here.
