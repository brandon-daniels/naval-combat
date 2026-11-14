# Sugar cane production

Open `Assets/scenes/naval_gameplay.scene` in S&box. Expand **Beacon Island trade buoy** in the hierarchy.

- **Goods barrel spawn - move me**: move and rotate this object to choose where finished physical cargo appears. The gold barrel in edit mode is a preview only. Put the marker above the ground or water, clear of walls and the buoy.
- **Sugar cane growing area - move me**: move and rotate this object to place the field. On its **Sugar Cane Production** component, set **Field Size**, **Rows**, **Columns**, **Cane Height**, and **Scythe Rotations Per Second**.
- Scrub **Preview Progress** from 0 to 1 to inspect growth, harvest, and packing. Enable **Animate Preview** to loop the complete 60-second sequence in the editor. Previewing never spends money or creates collectible cargo.
- The **Voyage Port** component on the buoy references both objects through **Barrel Spawn** and **Cane Field**. To author another production buoy, add the same components and assign its own markers.

During Play, bring the ship into the buoy's trade radius and slow below 100. Leave the helm, step clear of ship stations, and press **E** to open the investment screen. Choose **Invest $100** or **Cancel**. Barrels and ship stations retain priority over opening the screen, so you can still operate the ship near a buoy.

The production cycle grows cane for 40 seconds, cuts it with two spinning scythes for 12 seconds, then moves the harvested cane into a barrel for 8 seconds. At 60 seconds, the preview becomes a real physical barrel owned by the investor. The server validates payment, distance, ship speed, and whether the growing area is busy. Only one batch uses a given field at a time. Finished loose barrels collide, fall, and float on the ocean; carrying and securing cargo still use the existing E interaction.

`naval_test_production` runs the real-time local editor diagnostic. Start a fresh Play session and allow the player to initialize before running it. Restart Play after testing. It checks cancellation, control restoration, payment, duplicate/overlapping investment rejection, the actual 60-second phases, spawn placement, collision, and flotation.
