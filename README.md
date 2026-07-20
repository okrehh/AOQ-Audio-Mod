# AoQAudioMod

A sound overhaul / sound effect replacement mod for the PCVR version of Attack on Quest built with BepInEx 5.

# Features

\- More faithful / accurate sounds for ODM, titans, etc.

\- A sounds folder by which sounds can be read to be replaced in game

# Installation

\- Put the AOQAudioMod.dll file in the BepInEx plugins folder

\- To use the built-in sound pack, place the linked 'Sounds' folder in plugins as well (placing none will create an empty 'Sounds' folder)

# Requirements

\- BepInEx 5.4.x for AoQ

# Implementing custom sounds

\- Sound files must be the following:

&#x20; - .wav

&#x20; - 44100hz sampling rate

&#x20; - 16 bit encoding

- stereo

&#x20; - MUST have the same name as the original file! However, you can assign multiple sounds to one action or event in game by adding \_1, \_2, \_3, etc. to the end of the file name. Doing so will have one of the sounds played at random each time the event occurs

&#x20; - original sound file names for reference can be viewed by loading the AttackOnQuest\_Data folder in a program such as AssetStudio

&#x20; - anything different is not guaranteed to work!



