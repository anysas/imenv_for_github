#!/usr/bin/env python3
"""
Generate vocabulary.json for the Algorithmic Gallery prompt parser.

Builds a 10,000+ word dictionary mapping English words to emotional tags,
intent categories, and confidence weights. Ships as static JSON — no runtime
API calls needed.

Modes:
  python3 generate_vocabulary.py              # built-in seed vocab + prop names
  python3 generate_vocabulary.py --expand     # also expand via Claude API (needs ANTHROPIC_API_KEY)
  python3 generate_vocabulary.py --stats      # print tag distribution stats only

Run from the project root.
"""

import json
import os
import re
import sys
from pathlib import Path
from collections import Counter
from datetime import date

OUTPUT_PATH = Path("Assets/StreamingAssets/vocabulary.json")
MANIFEST_PATH = Path("Assets/StreamingAssets/curated-props.json")

VALID_EMOTIONAL = {
    "intimate", "nostalgic", "personal", "comforting", "domestic",
    "clinical", "institutional", "bureaucratic", "threatening",
    "melancholy", "abandoned", "decayed", "sacred", "liminal",
    "public", "mundane"
}

VALID_GROUPS = {"domestic", "furniture", "item", "lab", "office", "retail", "tech", "workshop"}

VALID_SETTINGS = {
    "home", "office", "workshop", "lab", "retail", "public", "sacred", "liminal"
}

VALID_STYLES = {
    "cozy", "cold", "warm", "clean", "messy", "clinical", "nostalgic", "sacred",
    "abandoned", "decayed", "bright", "dark", "quiet", "crowded", "safe", "threatening"
}

# ---------------------------------------------------------------------------
# SEED VOCABULARY — comprehensive built-in word→tag mappings
# ---------------------------------------------------------------------------
# Format: dict with keys:
#   emotional: [str]  — required, 1-4 tags from VALID_EMOTIONAL
#   object: str|None  — optional, one of VALID_GROUPS
#   setting: str|None — optional, one of VALID_SETTINGS
#   action: str|None  — optional, action verb category
#   style: str|None   — optional, one of VALID_STYLES
#   weight: float     — confidence 0.5-1.0
#   words: str        — space-separated word list

SEED = [
    # =====================================================================
    # EMOTIONS & PSYCHOLOGICAL STATES
    # =====================================================================

    # Positive emotions → comforting, intimate
    {"emotional": ["comforting", "intimate"], "style": "warm", "weight": 0.8,
     "words": "happy happiness joy joyful joyous content contentment pleased pleasure "
              "delighted delight glad cheerful blissful bliss euphoric euphoria serene "
              "serenity peaceful peace tranquil calm calming soothed soothing gentle "
              "tenderness tender affection affectionate warmth cozy snug embrace "
              "hugged hug cuddled cuddle caress cherished cherish grateful gratitude "
              "relieved relief hopeful hope optimistic satisfied fulfillment fulfilled"},

    # Negative emotions → melancholy
    {"emotional": ["melancholy", "personal"], "weight": 0.85,
     "words": "sad sadness sorrow sorrowful grief grieving mourn mourning "
              "heartbreak heartbroken devastated despair despairing hopeless "
              "anguish agonizing agony dejected desolate miserable wretched "
              "forlorn bereft inconsolable lamentation weeping wept sobbing "
              "tears tearful cried crying regret regretful remorse yearning "
              "longing pining wistful bittersweet melancholic melancholia"},

    # Fear & anxiety → threatening
    {"emotional": ["threatening"], "weight": 0.85,
     "words": "afraid fear fearful scared scary terrified terror horrified horror "
              "nightmare nightmarish dread dreading panic panicked panicking anxious "
              "anxiety worried worry apprehensive uneasy unease foreboding ominous "
              "menacing sinister eerie creepy unsettling disturbing alarming alarmed "
              "startled spooked haunted haunting chilling petrified paralyzed "
              "phobia claustrophobic agoraphobic paranoid paranoia suspicious"},

    # Anger & frustration → threatening, institutional
    {"emotional": ["threatening", "institutional"], "weight": 0.75,
     "words": "angry anger furious rage enraged hostile hostility aggressive "
              "resentment resentful bitter bitterness frustrated frustration "
              "indignant outraged contempt disgusted revulsion hatred spite "
              "vengeful vengeance wrathful defiant oppressed oppression"},

    # Loneliness & isolation → abandoned, intimate
    {"emotional": ["abandoned", "intimate"], "weight": 0.85,
     "words": "lonely loneliness isolated isolation solitary solitude withdrawn "
              "disconnected alienated alienation estranged detached outcast "
              "forsaken unwanted neglected overlooked invisible excluded "
              "hermit recluse sequestered secluded seclusion"},

    # Shame & vulnerability → personal, institutional
    {"emotional": ["personal", "institutional"], "weight": 0.75,
     "words": "shame ashamed humiliated humiliation embarrassed vulnerable "
              "exposed naked stripped judged scrutinized examined evaluated "
              "inadequate unworthy inferior worthless powerless helpless "
              "dependent submissive compliant obedient controlled"},

    # Nostalgia-specific emotional states
    {"emotional": ["nostalgic", "melancholy"], "weight": 0.9,
     "words": "nostalgia nostalgic reminisce reminiscing reminiscence wistful "
              "bittersweet sentimental longing yearning pining homesick homesickness "
              "bygone yesteryear olden retro vintage throwback flashback"},

    # =====================================================================
    # FAMILY, RELATIONSHIPS & PEOPLE
    # =====================================================================

    {"emotional": ["personal", "nostalgic", "intimate"], "weight": 1.0,
     "words": "mother mom mama mum mommy father dad papa daddy parent parents "
              "grandmother grandma granny nana grandfather grandpa grandad "
              "family brother sister sibling son daughter child children kid "
              "baby infant toddler teen teenager aunt uncle cousin nephew niece "
              "spouse husband wife partner lover boyfriend girlfriend fiancee"},

    {"emotional": ["personal", "intimate"], "weight": 0.85,
     "words": "friend friendship companion companionship neighbor neighbour "
              "acquaintance colleague roommate housemate flatmate soulmate "
              "confidant mentor protector guardian caretaker caregiver"},

    {"emotional": ["melancholy", "nostalgic", "personal"], "weight": 0.9,
     "words": "widow widower orphan orphaned bereaved deceased departed "
              "estranged divorced separated abandoned lost missing gone "
              "ancestor descendant lineage heritage bloodline generation"},

    # =====================================================================
    # BODY & PHYSICAL
    # =====================================================================

    {"emotional": ["personal", "intimate"], "weight": 0.7,
     "words": "hand hands finger fingers palm wrist arm arms shoulder shoulders "
              "face eyes eye mouth lips skin hair body chest heart lungs "
              "breath breathing voice whisper touch feeling sensation "
              "pulse heartbeat veins blood bones skeleton skull"},

    {"emotional": ["clinical", "personal"], "weight": 0.75,
     "words": "brain nerve spine muscle tissue organ stomach intestine kidney "
              "liver cells anatomy physiology ailment symptom diagnosis "
              "wound scar bruise fracture swollen inflammation fever"},

    # =====================================================================
    # MEMORY & TIME
    # =====================================================================

    {"emotional": ["nostalgic", "personal"], "weight": 0.9,
     "words": "remember remembering memory memories recollection recalling "
              "memoir reminisce flashback recall unforgettable memorable "
              "forgotten forgetfulness fading vanishing disappearing"},

    {"emotional": ["nostalgic", "melancholy"], "weight": 0.8,
     "words": "past before yesterday yesteryear ago once formerly previously "
              "ancient old older oldest antique vintage relic artifact "
              "heritage tradition legacy inheritance heirloom keepsake "
              "souvenir memento token trophy relic remnant trace vestige"},

    {"emotional": ["liminal", "mundane"], "weight": 0.7,
     "words": "now present moment today current contemporary modern recent "
              "temporary transient fleeting ephemeral passing brief instant "
              "meanwhile interim intermission pause interval between"},

    {"emotional": ["threatening", "melancholy"], "weight": 0.75,
     "words": "future tomorrow eventual inevitable unavoidable impending "
              "countdown deadline expiration ticking running dwindling "
              "forever eternal eternity permanent irreversible never"},

    # =====================================================================
    # DEATH & MORTALITY
    # =====================================================================

    {"emotional": ["melancholy", "sacred"], "weight": 0.95,
     "words": "death dead died dying die mortal mortality perish perished "
              "grave graveyard cemetery tombstone headstone epitaph burial "
              "coffin casket funeral wake eulogy mourning grieving bereaved "
              "afterlife ghost spirit phantom specter apparition wraith "
              "skull remains ashes cremation urn memorial tribute obituary"},

    # =====================================================================
    # HOME & DOMESTIC SPACES
    # =====================================================================

    {"emotional": ["domestic", "intimate"], "setting": "home", "weight": 0.9,
     "words": "home house apartment flat dwelling residence abode shelter "
              "living bedroom bathroom kitchen dining room attic basement "
              "cellar garage porch balcony patio yard garden backyard "
              "nursery playroom den study foyer hallway landing closet "
              "pantry laundry mudroom sunroom conservatory"},

    {"emotional": ["comforting", "domestic"], "object": "furniture", "weight": 0.85,
     "words": "couch sofa loveseat armchair recliner rocking chair rocker "
              "bed mattress pillow cushion blanket quilt duvet comforter "
              "sheets linens bedspread throw rug carpet runner mat "
              "curtain drapes blinds valance tapestry wallpaper"},

    {"emotional": ["domestic", "mundane"], "object": "domestic", "weight": 0.8,
     "words": "table desk counter countertop shelf shelving bookcase "
              "cabinet cupboard wardrobe dresser drawer nightstand "
              "credenza sideboard hutch armoire chest vanity mirror "
              "mantle mantelpiece hearth fireplace chimney stove oven "
              "refrigerator fridge freezer dishwasher microwave toaster "
              "sink faucet bathtub shower toilet plumbing radiator"},

    # =====================================================================
    # DOMESTIC OBJECTS & EVERYDAY ITEMS
    # =====================================================================

    {"emotional": ["mundane", "domestic"], "object": "item", "weight": 0.75,
     "words": "plate dish bowl cup mug glass utensil fork knife spoon "
              "pot pan skillet kettle teapot coffeepot pitcher carafe "
              "napkin towel washcloth sponge soap detergent cleaner "
              "broom mop bucket vacuum dustpan trash garbage bin basket "
              "hamper ironing board clothesline hanger hook peg rack"},

    {"emotional": ["personal", "nostalgic"], "object": "item", "weight": 0.85,
     "words": "photograph photo picture portrait snapshot polaroid album "
              "scrapbook diary journal notebook letter envelope postcard "
              "stamp card invitation keepsake souvenir memento trinket "
              "jewelry ring necklace bracelet watch locket pendant brooch "
              "glasses spectacles wallet purse handbag briefcase"},

    {"emotional": ["intimate", "comforting"], "object": "item", "weight": 0.8,
     "words": "lamp lantern candle candlestick chandelier sconce nightlight "
              "lightbulb lightswitch dimmer fairy string glow glowing "
              "book books novel paperback hardcover reading bookmark "
              "magazine newspaper comic puzzle game boardgame cards dice"},

    {"emotional": ["mundane", "personal"], "object": "item", "weight": 0.7,
     "words": "phone telephone cellphone mobile smartphone receiver cradle "
              "remote control charger cable cord plug adapter battery "
              "clock alarm timer calendar planner schedule appointment "
              "key keychain lock padlock bolt latch doorknob handle"},

    # =====================================================================
    # FOOD & COOKING
    # =====================================================================

    {"emotional": ["comforting", "domestic"], "object": "domestic", "action": "cook",
     "weight": 0.8,
     "words": "food meal dinner lunch breakfast supper feast snack treat "
              "cooking baking roasting frying boiling simmering steaming "
              "recipe ingredients preparation dough batter flour sugar "
              "butter eggs milk cream cheese bread toast pastry cake pie "
              "cookie biscuit soup stew broth noodle pasta rice grain "
              "fruit vegetable salad meat chicken beef pork fish seafood "
              "chocolate vanilla cinnamon spice herb seasoning sauce "
              "tea coffee cocoa juice water lemonade wine beer"},

    # =====================================================================
    # NATURE & OUTDOORS
    # =====================================================================

    {"emotional": ["comforting", "nostalgic"], "weight": 0.75,
     "words": "tree trees forest woods woodland grove orchard meadow "
              "field clearing glade valley glen hill hillside slope "
              "mountain peak summit ridge trail path footpath dirt "
              "river stream creek brook waterfall pond lake reservoir "
              "garden flower flowers bloom blossom petal leaf leaves "
              "branch twig root bark moss fern vine ivy hedge bush shrub "
              "grass lawn clover dandelion wildflower sunflower rose tulip"},

    {"emotional": ["sacred", "intimate"], "weight": 0.75,
     "words": "moonlight starlight starry constellation aurora twilight "
              "dawn sunrise sunset dusk horizon sky celestial cosmic "
              "universe galaxy nebula orbit eclipse solstice equinox"},

    {"emotional": ["threatening", "abandoned"], "weight": 0.75,
     "words": "wilderness jungle swamp marsh bog quicksand thorns bramble "
              "thicket undergrowth overgrown tangled choked strangled "
              "desert wasteland barren desolate bleak arid scorched "
              "volcano eruption earthquake tremor landslide avalanche "
              "flood tsunami hurricane tornado storm tempest blizzard"},

    # =====================================================================
    # WEATHER & ATMOSPHERE
    # =====================================================================

    {"emotional": ["comforting", "intimate"], "style": "warm", "weight": 0.75,
     "words": "sunshine sunlight sunny warmth warm golden amber glow "
              "spring breezy mild pleasant balmy temperate"},

    {"emotional": ["melancholy", "liminal"], "weight": 0.75,
     "words": "rain raining rainy drizzle downpour rainfall puddle "
              "cloudy overcast grey gray misty mist foggy fog haze hazy "
              "damp humid muggy dripping drizzling patter droplets"},

    {"emotional": ["abandoned", "decayed"], "style": "cold", "weight": 0.75,
     "words": "cold freezing frozen frost frosty ice icy sleet snow snowy "
              "winter blizzard snowfall snowflake icicle permafrost "
              "bitter chilly frigid numb numbing windchill"},

    {"emotional": ["threatening"], "weight": 0.7,
     "words": "thunder lightning storm stormy tempest gale howling wind "
              "tornado hurricane cyclone whirlwind downpour deluge "
              "darkness blackout shadow shadows pitch eclipse"},

    # =====================================================================
    # ANIMALS & CREATURES
    # =====================================================================

    {"emotional": ["comforting", "domestic"], "weight": 0.7,
     "words": "cat kitten dog puppy pet hamster rabbit bunny goldfish "
              "bird canary parakeet parrot songbird sparrow robin wren "
              "horse pony lamb sheep goat chicken hen rooster duck goose "
              "cow cattle pig piglet donkey butterfly bee ladybug firefly"},

    {"emotional": ["threatening"], "weight": 0.7,
     "words": "wolf wolves spider snake serpent scorpion rat rats rodent "
              "crow raven vulture hawk predator prey hunter stalker "
              "shark teeth claws fangs venom poison stinger swarm hive "
              "cockroach centipede maggot worm parasite leech tick"},

    {"emotional": ["nostalgic", "sacred"], "weight": 0.7,
     "words": "owl dove eagle fox deer stag antler fawn bear cub salmon "
              "whale dolphin turtle tortoise moth dragonfly cricket frog "
              "heron crane swan peacock phoenix mythical creature beast"},

    # =====================================================================
    # CLOTHING & TEXTILES
    # =====================================================================

    {"emotional": ["personal", "mundane"], "weight": 0.7,
     "words": "shirt blouse sweater jumper cardigan jacket coat overcoat "
              "dress skirt pants trousers jeans shorts uniform apron "
              "hat cap scarf gloves mittens socks shoes boots slippers "
              "sneakers sandals laces belt buckle button zipper thread "
              "needle pin thimble sewing knitting crochet yarn wool "
              "cotton linen silk satin velvet leather denim flannel lace"},

    {"emotional": ["nostalgic", "personal"], "weight": 0.8,
     "words": "handkerchief heirloom quilt patchwork embroidery handmade "
              "homespun knitted crocheted darned mended patched worn "
              "faded threadbare tattered ragged frayed moth-eaten vintage"},

    # =====================================================================
    # SOUNDS & MUSIC
    # =====================================================================

    {"emotional": ["intimate", "comforting"], "weight": 0.75,
     "words": "whisper murmur hum humming lullaby melody tune song singing "
              "music musical harmony chord note rhythm beat tempo "
              "piano violin cello guitar acoustic strings flute harp "
              "vinyl record turntable radio static frequency dial "
              "chime bell bells ringing ticking clock grandfather wind"},

    {"emotional": ["threatening", "liminal"], "weight": 0.75,
     "words": "silence silent hush hushed muffled muted deadened "
              "echo echoing reverb resonance vibration drone buzz "
              "screech scream shriek wail siren alarm bang crash "
              "creak creaking groan groaning rattle rattling thud "
              "footsteps dripping tapping scratching whispering"},

    {"emotional": ["mundane", "public"], "weight": 0.65,
     "words": "noise noisy loud blaring cacophony clamor chatter "
              "commotion hubbub din racket traffic honking beeping "
              "announcement intercom speaker microphone amplified "
              "television broadcast program channel signal reception"},

    # =====================================================================
    # SMELLS & SENSORY
    # =====================================================================

    {"emotional": ["comforting", "nostalgic"], "weight": 0.8,
     "words": "smell scent fragrance aroma perfume cologne incense "
              "baking cookies cinnamon vanilla lavender rose jasmine "
              "cedar pine woodsmoke campfire fireplace petrichor "
              "freshly fresh laundry linen soap shampoo lotion"},

    {"emotional": ["clinical", "institutional"], "weight": 0.75,
     "words": "antiseptic disinfectant bleach ammonia chlorine sterile "
              "sanitized chemical formaldehyde rubbing alcohol latex "
              "surgical stainless steel medicinal pharmaceutical"},

    {"emotional": ["decayed", "abandoned"], "weight": 0.8,
     "words": "stench stink rotten putrid rancid fetid musty moldy "
              "mildew damp decay decomposing compost sewage sulfur "
              "stale smoke smoky ash soot acrid pungent bitter"},

    # =====================================================================
    # TEXTURES & MATERIALS
    # =====================================================================

    {"emotional": ["comforting", "intimate"], "weight": 0.7,
     "words": "soft fluffy plush fuzzy velvety silky smooth supple "
              "cushioned padded quilted stuffed feathery downy warm "
              "woven knit braided crocheted felted spun threaded"},

    {"emotional": ["clinical", "institutional"], "style": "cold", "weight": 0.7,
     "words": "hard rigid stiff firm cold metallic chrome steel aluminum "
              "stainless titanium glass plastic acrylic fiberglass resin "
              "porcelain ceramic tile linoleum laminate formica vinyl "
              "polished buffed gleaming sterile sealed packaged wrapped"},

    {"emotional": ["nostalgic", "domestic"], "weight": 0.7,
     "words": "wood wooden timber oak pine maple walnut cedar mahogany "
              "plywood particle board grain knot bark lumber plank beam "
              "brick stone masonry mortar concrete cobblestone gravel "
              "clay pottery stoneware earthenware terracotta glazed fired"},

    {"emotional": ["decayed", "abandoned"], "weight": 0.8,
     "words": "rust rusted rusty corroded tarnished oxidized pitted "
              "cracked chipped peeling flaking crumbling powdery brittle "
              "warped bent twisted buckled dented scratched scored gouged "
              "stained discolored yellowed browned faded bleached weathered"},

    # =====================================================================
    # COLORS & LIGHT
    # =====================================================================

    {"emotional": ["comforting", "nostalgic"], "style": "warm", "weight": 0.65,
     "words": "red crimson scarlet ruby burgundy maroon rose pink blush "
              "orange amber tangerine peach coral salmon apricot copper "
              "yellow golden honey mustard lemon cream ivory wheat tan "
              "brown chocolate chestnut mahogany sienna umber sepia"},

    {"emotional": ["clinical", "liminal"], "style": "cold", "weight": 0.65,
     "words": "white blank sterile pale ghost bone chalk snow frost "
              "silver chrome platinum grey gray slate charcoal ash "
              "blue navy cobalt cerulean azure teal turquoise indigo "
              "green emerald jade olive sage moss mint seafoam hunter"},

    {"emotional": ["threatening", "melancholy"], "style": "dark", "weight": 0.7,
     "words": "black dark darkness shadow shadowy dim dimmed murky gloomy "
              "obsidian onyx ebony midnight pitch ink void abyss deep "
              "purple violet plum aubergine bruised blackened soot"},

    {"emotional": ["intimate", "sacred"], "weight": 0.7,
     "words": "glow glowing luminous radiant luminescent phosphorescent "
              "shimmering glimmer glint sparkle twinkle flicker flickering "
              "candlelit firelit moonlit sunlit dappled filtered diffused "
              "halo aureole nimbus iridescent opalescent pearlescent"},

    # =====================================================================
    # ARCHITECTURE & BUILT ENVIRONMENT
    # =====================================================================

    {"emotional": ["liminal"], "setting": "liminal", "weight": 0.9,
     "words": "door doorway doorframe threshold gateway entrance exit "
              "hallway corridor passage passageway tunnel arch archway "
              "stairs stairway staircase stairwell landing step steps "
              "elevator escalator lift ramp bridge walkway overpass "
              "window windowsill windowpane skylight transom vestibule "
              "foyer lobby anteroom airlock portal turnstile barrier"},

    {"emotional": ["institutional", "mundane"], "setting": "office", "weight": 0.8,
     "words": "building structure tower skyscraper highrise complex "
              "floor ceiling wall walls roof foundation beam column "
              "pillar support girder joist rafter truss frame framework "
              "facade exterior interior blueprint floorplan layout "
              "construction scaffolding crane excavation demolition"},

    {"emotional": ["abandoned", "decayed"], "weight": 0.85,
     "words": "ruin ruins ruined rubble wreckage debris remnant remains "
              "crumbling collapsed dilapidated derelict condemned "
              "boarded shuttered closed condemned vacant empty hollow "
              "overgrown reclaimed deteriorated neglected forsaken"},

    # =====================================================================
    # CLINICAL & MEDICAL
    # =====================================================================

    {"emotional": ["clinical", "institutional"], "setting": "lab", "weight": 0.9,
     "words": "hospital clinic ward emergency room operating room surgery "
              "examination exam checkup diagnosis prognosis treatment "
              "therapy rehabilitation recovery intensive care unit icu "
              "nurse doctor physician surgeon specialist consultant "
              "patient bed gurney stretcher wheelchair crutch walker "
              "waiting room appointment chart record file prescription"},

    {"emotional": ["clinical", "threatening"], "object": "lab", "weight": 0.9,
     "words": "syringe needle injection shot vaccine blood draw sample "
              "scalpel blade instrument probe catheter tube drip infusion "
              "monitor beeping flatline resuscitation defibrillator "
              "anesthesia sedation unconscious ventilator respirator "
              "specimen jar formaldehyde preservative autopsy morgue "
              "x-ray scan mri ct ultrasound biopsy test results"},

    {"emotional": ["clinical", "mundane"], "object": "lab", "weight": 0.8,
     "words": "medicine medication pill tablet capsule prescription "
              "pharmacy drugstore dose dosage side effects treatment "
              "bandage gauze tape splint cast sling brace support "
              "thermometer stethoscope scale chart vitals pulse "
              "insurance copay deductible billing waiting referral"},

    # =====================================================================
    # SACRED & SPIRITUAL
    # =====================================================================

    {"emotional": ["sacred", "intimate"], "setting": "sacred", "weight": 0.95,
     "words": "church chapel cathedral temple mosque synagogue shrine "
              "altar sanctuary nave aisle transept apse baptistry "
              "pew kneeler pulpit lectern tabernacle font crucifix "
              "stained glass rosette dome spire steeple bell tower "
              "cloister monastery convent abbey priory hermitage "
              "grotto niche alcove reliquary mausoleum crypt vault"},

    {"emotional": ["sacred", "intimate"], "weight": 0.9,
     "words": "prayer praying worship meditation contemplation devotion "
              "faith belief spirit spiritual soul divine holy blessed "
              "sacred hallowed consecrated anointed ordained ritual "
              "ceremony sacrament communion baptism confirmation "
              "confession absolution penance pilgrimage vigil vespers "
              "hymn psalm chant mantra litany invocation benediction "
              "candle votive incense offering tithe alms charity grace"},

    {"emotional": ["sacred", "nostalgic"], "weight": 0.85,
     "words": "angel archangel seraph cherub saint martyr prophet apostle "
              "miracle revelation vision prophecy parable testament "
              "scripture gospel bible quran torah talmud sutra dharma "
              "heaven paradise eden nirvana salvation redemption resurrection "
              "creation genesis exodus covenant commandment"},

    {"emotional": ["sacred", "melancholy"], "weight": 0.85,
     "words": "sin confession repentance atonement sacrifice offering "
              "purgatory limbo judgement reckoning tribulation suffering "
              "crucifixion martyrdom persecution exile wandering lost "
              "fallen cursed damned forbidden temptation serpent"},

    # =====================================================================
    # BUREAUCRATIC & INSTITUTIONAL
    # =====================================================================

    {"emotional": ["bureaucratic", "institutional"], "setting": "office", "weight": 0.95,
     "words": "office cubicle desk workstation partition divider "
              "paperwork document form application filing cabinet "
              "folder binder clipboard ledger spreadsheet report memo "
              "memorandum correspondence letter notice bulletin "
              "stamp seal signature endorsement notarized certified "
              "copy duplicate triplicate original archived filed "
              "approval denial rejection pending processing review "
              "regulation policy procedure protocol guideline standard"},

    {"emotional": ["bureaucratic", "institutional"], "weight": 0.9,
     "words": "government agency department bureau ministry commission "
              "committee board council authority administration "
              "official administrator bureaucrat clerk officer inspector "
              "auditor assessor examiner evaluator coordinator supervisor "
              "director manager executive secretary receptionist "
              "appointment scheduled reserved allocated assigned designated "
              "permit license registration certificate credential badge "
              "identification verification authentication authorization"},

    {"emotional": ["bureaucratic", "mundane"], "weight": 0.85,
     "words": "queue line number ticket waiting called served next "
              "please take seat fill out complete submit return "
              "deadline extension renewal expiration expired overdue "
              "fee fine penalty surcharge tax duty levy assessment "
              "invoice receipt statement balance account record entry "
              "database system network server terminal login password "
              "error denied access restricted classified confidential"},

    {"emotional": ["institutional", "bureaucratic"], "weight": 0.8,
     "words": "law legal court courthouse judge jury trial hearing "
              "verdict sentence appeal conviction acquittal parole "
              "probation bail bond warrant subpoena summons citation "
              "attorney lawyer counsel defendant plaintiff prosecution "
              "evidence exhibit testimony deposition affidavit oath "
              "statute ordinance code amendment clause provision contract "
              "agreement terms conditions liability waiver disclaimer"},

    # =====================================================================
    # WORKPLACE & OFFICE
    # =====================================================================

    {"emotional": ["mundane", "institutional"], "setting": "office", "object": "office",
     "weight": 0.8,
     "words": "workplace job career profession occupation employment "
              "employer employee staff workforce personnel team department "
              "meeting conference call presentation pitch proposal "
              "deadline project task assignment report email inbox "
              "printer copier scanner fax shredder stapler paperclip "
              "whiteboard marker eraser projector screen podium"},

    {"emotional": ["mundane", "bureaucratic"], "weight": 0.75,
     "words": "commute commuting transit bus train subway metro tram "
              "carpool parking lot garage elevator lobby badge swipe "
              "timecard punch clock shift overtime break lunch room "
              "vending machine water cooler coffee maker kitchenette "
              "fluorescent overhead lighting buzzing humming drone"},

    # =====================================================================
    # EDUCATION & SCHOOL
    # =====================================================================

    {"emotional": ["institutional", "nostalgic"], "weight": 0.8,
     "words": "school classroom teacher student pupil desk chalk board "
              "blackboard chalkboard whiteboard textbook notebook pencil "
              "eraser ruler compass protractor backpack lunchbox recess "
              "playground swing slide monkey bars sandbox hopscotch "
              "homework assignment test exam quiz grade report card "
              "principal counselor librarian library cafeteria gymnasium"},

    {"emotional": ["institutional", "mundane"], "weight": 0.75,
     "words": "university college campus dormitory lecture hall auditorium "
              "laboratory seminar tutorial thesis dissertation research "
              "professor dean registrar enrollment transcript diploma "
              "degree graduation commencement ceremony gown cap tassel"},

    # =====================================================================
    # CHILDHOOD & PLAY
    # =====================================================================

    {"emotional": ["nostalgic", "personal", "intimate"], "weight": 0.9,
     "words": "childhood boyhood girlhood youth young younger kid kiddo "
              "toddler infant baby newborn cradle crib mobile nursery "
              "playroom treehouse fort blanket fort pillow fort hideout "
              "imaginary pretend make-believe fantasy adventure treasure "
              "birthday party candles cake presents wrapping ribbon bow"},

    {"emotional": ["nostalgic", "personal"], "object": "item", "weight": 0.85,
     "words": "toy toys doll dolls teddy bear stuffed animal plush "
              "figurine action figure puppet marionette jack-in-the-box "
              "blocks lego puzzle jigsaw crayon marker coloring drawing "
              "playdough clay sticker stamp balloon bubble pinwheel "
              "kite swing slide seesaw sandbox sandbox castle bucket "
              "jump rope hula hoop bicycle tricycle scooter wagon sled"},

    # =====================================================================
    # TECHNOLOGY & MACHINES
    # =====================================================================

    {"emotional": ["institutional", "mundane"], "object": "tech", "weight": 0.8,
     "words": "computer monitor screen keyboard mouse cursor desktop "
              "laptop tablet smartphone device gadget hardware software "
              "processor chip circuit motherboard drive storage memory "
              "server rack cable wire network router modem antenna signal "
              "printer scanner projector speaker headphones microphone "
              "camera webcam sensor detector scanner barcode reader"},

    {"emotional": ["institutional", "threatening"], "weight": 0.8,
     "words": "algorithm system automated automation robot robotic "
              "artificial intelligence machine learning neural network "
              "surveillance camera tracking monitoring recording logged "
              "data collected stored analyzed processed profiled targeted "
              "notification alert warning error crash malfunction glitch "
              "overload meltdown failure shutdown terminated disconnected"},

    {"emotional": ["mundane", "nostalgic"], "object": "tech", "weight": 0.7,
     "words": "television tv remote channel antenna rabbit ears static "
              "vcr vhs tape cassette rewinding tracking floppy disk "
              "diskette cdrom dvd walkman boombox stereo speakers "
              "dial rotary payphone booth answering machine beep "
              "typewriter ribbon correction tape carbon paper dot matrix"},

    # =====================================================================
    # TOOLS & WORKSHOP
    # =====================================================================

    {"emotional": ["mundane", "personal"], "object": "workshop", "setting": "workshop",
     "weight": 0.8,
     "words": "tool tools hammer nail screw screwdriver wrench pliers "
              "saw blade drill bit level tape measure ruler square "
              "clamp vise anvil forge welding torch soldering iron "
              "sandpaper file rasp chisel gouge plane lathe router "
              "workbench sawhorse ladder scaffold toolbox toolchest "
              "workshop garage shed barn studio workspace bench"},

    {"emotional": ["personal", "nostalgic"], "object": "workshop", "weight": 0.8,
     "words": "craft crafting handmade homemade built making building "
              "repair fixing mending restoring restoration woodworking "
              "carpentry joinery cabinetry upholstery refinishing "
              "carving whittling turning shaping molding casting "
              "welding brazing forging hammering bending cutting"},

    # =====================================================================
    # RETAIL & COMMERCE
    # =====================================================================

    {"emotional": ["public", "mundane"], "setting": "retail", "object": "retail",
     "weight": 0.8,
     "words": "store shop market mall plaza arcade gallery showroom "
              "boutique emporium department storefront display window "
              "shelf aisle register checkout counter cashier receipt "
              "bag cart basket trolley shopping browsing customer "
              "sale clearance discount price tag barcode scanner "
              "mannequin hanger rack fitting room dressing room mirror"},

    {"emotional": ["public", "mundane"], "weight": 0.7,
     "words": "supermarket grocery produce dairy frozen bakery deli "
              "butcher pharmacy convenience corner bodega kiosk stand "
              "stall vendor merchant peddler hawker pushcart market "
              "flea market thrift secondhand pawnshop antique dealer "
              "catalog order delivery package shipment warehouse stock"},

    # =====================================================================
    # URBAN & CITY
    # =====================================================================

    {"emotional": ["public", "mundane"], "setting": "public", "weight": 0.8,
     "words": "city town downtown uptown midtown district neighborhood "
              "block street avenue boulevard road lane alley sidewalk "
              "curb gutter drain manhole hydrant lamppost streetlight "
              "intersection crosswalk traffic signal sign stop yield "
              "parking meter bench bus stop shelter taxi cab ride fare"},

    {"emotional": ["public", "liminal"], "weight": 0.75,
     "words": "station terminal platform track rail railway subway metro "
              "underground tube depot hub junction transfer turnstile "
              "ticket booth gate boarding departing arriving connecting "
              "airport runway tarmac hangar terminal gate lounge "
              "port dock harbor pier wharf marina ferry terminal"},

    {"emotional": ["public", "institutional"], "weight": 0.75,
     "words": "plaza square park fountain statue monument obelisk "
              "museum gallery exhibit display archive vault repository "
              "library reading room stacks reference desk catalog "
              "theater auditorium stage balcony orchestra mezzanine "
              "arena stadium coliseum amphitheater grandstand bleachers"},

    # =====================================================================
    # RURAL & COUNTRYSIDE
    # =====================================================================

    {"emotional": ["nostalgic", "comforting"], "weight": 0.8,
     "words": "farm farmhouse farmyard barn silo granary stable paddock "
              "pasture field meadow orchard vineyard grove plantation "
              "cottage cabin lodge homestead ranch manor estate country "
              "countryside rural village hamlet hamlet town outskirts "
              "dirt road gravel lane fence gate stile hedgerow "
              "scarecrow windmill water mill pond creek bridge covered"},

    # =====================================================================
    # INDUSTRIAL & FACTORY
    # =====================================================================

    {"emotional": ["institutional", "mundane"], "setting": "workshop", "object": "workshop",
     "weight": 0.8,
     "words": "factory plant mill foundry refinery smelter forge furnace "
              "assembly line conveyor belt production manufacturing "
              "warehouse distribution loading dock freight shipping "
              "crane forklift pallet crate container drum barrel tank "
              "pipe pipeline valve gauge meter dial control panel switch "
              "generator turbine motor engine boiler compressor pump"},

    {"emotional": ["abandoned", "decayed", "institutional"], "weight": 0.85,
     "words": "smokestack chimney vent exhaust duct conduit sewer drain "
              "industrial wasteland brownfield contaminated polluted "
              "toxic hazardous waste dump landfill junkyard scrapyard "
              "salvage reclamation decommissioned mothballed shuttered"},

    # =====================================================================
    # DECAY & ENTROPY
    # =====================================================================

    {"emotional": ["decayed", "abandoned"], "style": "decayed", "weight": 0.9,
     "words": "decay decaying decompose decomposing decomposition rot "
              "rotting rotten putrid putrefaction mold moldy mildew "
              "fungus lichen moss mossy overgrown undergrowth weeds "
              "rust rusting corrosion corroded erosion eroded weathering "
              "crumble crumbling disintegrate disintegrating collapse "
              "dilapidated deteriorating degrading entropy dissolution"},

    {"emotional": ["abandoned", "melancholy"], "style": "abandoned", "weight": 0.9,
     "words": "abandoned deserted forsaken desolate vacant unoccupied "
              "uninhabited empty barren bleak stark derelict condemned "
              "boarded shuttered padlocked chained sealed blocked "
              "forgotten neglected overlooked discarded disposed thrown "
              "remnant leftover remainder debris wreckage aftermath "
              "ghost town ruins wasteland aftermath vestige trace"},

    # =====================================================================
    # LIMINAL SPACES & TRANSITIONS
    # =====================================================================

    {"emotional": ["liminal", "mundane"], "setting": "liminal", "weight": 0.9,
     "words": "waiting room lobby foyer vestibule antechamber anteroom "
              "reception area checkout departure gate boarding area "
              "queue line waiting called next serve counter window "
              "between neither nowhere somewhere anywhere elsewhere "
              "transition transitional temporary transient passing through "
              "in-between midway halfway intersection junction crossroads"},

    {"emotional": ["liminal", "threatening"], "weight": 0.85,
     "words": "backroom backrooms behind locked off-limits restricted "
              "access denied maintenance staff only authorized personnel "
              "emergency exit fire escape stairwell service entrance "
              "loading dock back alley dumpster behind the building "
              "parking garage underground tunnel basement sub-basement "
              "crawlspace attic roof rooftop ledge edge precipice brink"},

    {"emotional": ["liminal", "intimate"], "weight": 0.8,
     "words": "dawn dusk twilight threshold crossing boundary border "
              "edge margin fringe periphery outskirts verge brink "
              "surface beneath below above beyond within without "
              "opening closing beginning ending waking sleeping dreaming "
              "fading appearing disappearing emerging dissolving"},

    # =====================================================================
    # MILITARY & CONFLICT
    # =====================================================================

    {"emotional": ["threatening", "institutional"], "weight": 0.8,
     "words": "military army navy force troops soldiers marines guards "
              "barracks bunker fortress stronghold outpost checkpoint "
              "patrol surveillance reconnaissance intel classified "
              "weapon gun rifle ammunition grenade explosive mine trap "
              "armor shield helmet uniform dog tags medal rank order "
              "command obey deploy advance retreat surrender capture"},

    {"emotional": ["melancholy", "abandoned"], "weight": 0.85,
     "words": "war battle conflict combat casualties wounded fallen "
              "veteran memorial monument cenotaph wreath poppy tribute "
              "prisoner captive detainee camp internment liberation "
              "refugee displaced exile sanctuary asylum border crossing "
              "aftermath reconstruction rebuilding restoration recovery"},

    # =====================================================================
    # CONFINEMENT & CONTROL
    # =====================================================================

    {"emotional": ["threatening", "institutional"], "weight": 0.9,
     "words": "prison jail cell bars cage confined confinement locked "
              "detained arrested handcuffs shackles chains restraint "
              "solitary isolation punishment sentence condemned inmate "
              "warden guard tower fence barbed wire perimeter compound "
              "asylum institution ward committed involuntary detained "
              "interrogation interview questioning suspect accused"},

    # =====================================================================
    # WATER & OCEAN
    # =====================================================================

    {"emotional": ["liminal", "melancholy"], "weight": 0.75,
     "words": "ocean sea waves tide current undertow surf shore beach "
              "coast coastline cliff bluff headland cove inlet bay "
              "lagoon estuary delta marsh wetland mangrove shoal reef "
              "deep depths abyss trench fathom submerged sunken drowned "
              "shipwreck wreck hull anchor chain buoy lighthouse beacon"},

    {"emotional": ["comforting", "intimate"], "weight": 0.7,
     "words": "bath bathing soak wading puddle pool swimming floating "
              "drifting bobbing ripple gentle lapping splashing drip "
              "fountain spring well cistern rain shower mist spray dew"},

    # =====================================================================
    # FIRE & HEAT
    # =====================================================================

    {"emotional": ["comforting", "nostalgic"], "weight": 0.8,
     "words": "fire fireplace hearth flames crackling warmth campfire "
              "bonfire embers coals glowing smoldering kindling logs "
              "woodstove furnace radiator heater heated warming"},

    {"emotional": ["threatening", "decayed"], "weight": 0.8,
     "words": "inferno blaze wildfire arson burned burning scorched "
              "charred blackened singed smoked smoking ashes cinders "
              "ruined destroyed devastated consumed engulfed firestorm"},

    # =====================================================================
    # ACTIONS & VERBS
    # =====================================================================

    {"emotional": ["personal"], "action": "create", "weight": 0.7,
     "words": "build building create creating make making construct "
              "assemble compose design craft forge shape form mold "
              "sculpt carve draw paint write compose arrange organize "
              "invent imagine envision conceive plan draft sketch"},

    {"emotional": ["nostalgic", "personal"], "action": "remember", "weight": 0.85,
     "words": "remember remembering recall recalling miss missing "
              "cherish treasure honor commemorate revisit return "
              "look back reflect reflecting think thinking dream dreaming"},

    {"emotional": ["intimate"], "action": "protect", "weight": 0.8,
     "words": "protect protecting shelter sheltering guard guarding "
              "shield shielding defend defending keep keeping safe "
              "hold holding embrace embracing wrap wrapping comfort "
              "care caring nurture nurturing tend tending heal healing"},

    {"emotional": ["threatening"], "action": "hide", "weight": 0.8,
     "words": "hide hiding conceal concealing escape escaping flee "
              "fleeing run running chase chasing hunt hunting stalk "
              "stalking pursue pursuing trap trapping caught catching "
              "lurk lurking creep creeping sneak sneaking prowl prowling"},

    {"emotional": ["melancholy"], "action": "mourn", "weight": 0.85,
     "words": "mourn mourning grieve grieving weep weeping cry crying "
              "lament lamenting suffer suffering endure enduring bear "
              "bearing cope coping struggle struggling survive surviving"},

    {"emotional": ["mundane"], "action": "wait", "weight": 0.7,
     "words": "wait waiting sit sitting stand standing watch watching "
              "observe observing listen listening stare staring gaze "
              "gazing look looking notice noticing read reading browse "
              "browsing scroll scrolling click clicking type typing"},

    {"emotional": ["domestic", "mundane"], "action": "work", "weight": 0.65,
     "words": "clean cleaning wash washing scrub scrubbing sweep sweeping "
              "mop mopping dust dusting polish polishing iron ironing "
              "fold folding sort sorting stack stacking pack packing "
              "unpack unpacking carry carrying lift lifting move moving"},

    {"emotional": ["abandoned"], "action": "search", "weight": 0.8,
     "words": "search searching seek seeking find finding discover "
              "discovering explore exploring wander wandering roam "
              "roaming drift drifting lost losing stumble stumbling "
              "reach reaching grasp grasping touch touching feel feeling"},

    {"emotional": ["sacred"], "action": "pray", "weight": 0.9,
     "words": "pray praying worship worshipping meditate meditating "
              "kneel kneeling bow bowing chant chanting sing singing "
              "bless blessing consecrate consecrating sanctify anoint"},

    # =====================================================================
    # ADJECTIVES & DESCRIPTORS
    # =====================================================================

    # Warm / cozy descriptors
    {"emotional": ["comforting", "intimate"], "style": "cozy", "weight": 0.8,
     "words": "cozy comfortable snug homey homely inviting welcoming "
              "quaint charming pleasant lovely warm toasty heated "
              "sheltered protected enclosed nestled tucked wrapped "
              "familiar known trusted reliable steady constant"},

    # Cold / sterile descriptors
    {"emotional": ["clinical", "institutional"], "style": "cold", "weight": 0.8,
     "words": "sterile sanitized clean pristine immaculate spotless "
              "clinical cold cool crisp sharp precise exact measured "
              "calculated controlled regulated standardized uniform "
              "blank bare minimal stripped plain unadorned austere "
              "functional utilitarian practical efficient optimized"},

    # Threatening descriptors
    {"emotional": ["threatening"], "style": "threatening", "weight": 0.8,
     "words": "dangerous hazardous risky perilous treacherous precarious "
              "volatile unstable unpredictable chaotic turbulent violent "
              "aggressive hostile menacing intimidating imposing looming "
              "overwhelming oppressive suffocating claustrophobic closing "
              "narrow tight cramped confined restricted limited trapped"},

    # Nostalgic / old descriptors
    {"emotional": ["nostalgic"], "style": "nostalgic", "weight": 0.75,
     "words": "old ancient aged aging worn weathered faded vintage retro "
              "classic traditional conventional old-fashioned dated "
              "timeworn antique period historical ancestral generational "
              "yellowed browned foxed dog-eared creased wrinkled"},

    # Abandoned / neglected descriptors
    {"emotional": ["abandoned", "decayed"], "style": "abandoned", "weight": 0.85,
     "words": "empty vacant hollow void barren desolate bleak stark "
              "bare stripped gutted cleared demolished razed leveled "
              "neglected unkempt untended overgrown wild reclaimed "
              "forgotten buried hidden obscured covered concealed "
              "dusty cobwebbed grimy filthy soiled stained discolored"},

    # Scale & intensity
    {"emotional": ["mundane"], "weight": 0.6,
     "words": "small tiny little miniature compact diminutive petite "
              "large big huge enormous massive immense vast expansive "
              "tall short wide narrow long deep shallow thin thick heavy "
              "light bright dim faint pale vivid rich saturated muted "
              "loud quiet soft hard rough smooth sharp dull flat round"},

    # =====================================================================
    # ABSTRACT CONCEPTS
    # =====================================================================

    {"emotional": ["personal", "intimate"], "weight": 0.75,
     "words": "love trust truth honesty loyalty devotion commitment "
              "identity self belonging acceptance understanding empathy "
              "compassion kindness generosity forgiveness mercy patience "
              "freedom liberty independence autonomy choice agency power"},

    {"emotional": ["institutional", "threatening"], "weight": 0.75,
     "words": "control authority power hierarchy order obedience discipline "
              "surveillance monitoring oversight accountability compliance "
              "conformity uniformity standardization normalization pressure "
              "expectation obligation responsibility duty requirement "
              "punishment reward consequence judgment evaluation assessment"},

    {"emotional": ["abandoned", "melancholy"], "weight": 0.8,
     "words": "absence void emptiness nothingness silence stillness "
              "loneliness isolation separation distance gap space between "
              "boundary limit end ending closure finality conclusion "
              "loss disappearance erasure oblivion forgotten"},

    {"emotional": ["liminal", "sacred"], "weight": 0.75,
     "words": "mystery unknown unknowable uncertain ambiguous vague "
              "obscure hidden secret forbidden taboo unspeakable "
              "infinite eternal boundless limitless vast immeasurable "
              "transformation metamorphosis change transition evolution "
              "threshold crossing passage journey pilgrimage quest"},

    # =====================================================================
    # MISC SUPPLEMENTAL — words that fall through category cracks
    # =====================================================================

    {"emotional": ["mundane", "public"], "object": "item", "weight": 0.65,
     "words": "umbrella newspaper magazine pamphlet flyer brochure map "
              "ticket stub pass token coin cash wallet card plastic "
              "cigarette lighter ashtray match matchbook gum wrapper "
              "pen pencil marker crayon paper envelope postage stamp"},

    {"emotional": ["nostalgic", "intimate"], "object": "item", "weight": 0.8,
     "words": "music box snow globe globe atlas compass telescope "
              "binoculars magnifying glass kaleidoscope prism crystal "
              "marble stone pebble shell feather leaf pressed flower "
              "ribbon bow string twine rope chain link knot button"},

    {"emotional": ["institutional", "liminal"], "weight": 0.7,
     "words": "sign signage placard notice warning caution danger "
              "exit entrance push pull open closed hours reserved "
              "occupied vacant out of order under construction "
              "restricted authorized personnel only keep out no entry"},
]


# ---------------------------------------------------------------------------
# INDIVIDUAL WORD OVERRIDES — specific words needing custom tag combos
# ---------------------------------------------------------------------------
OVERRIDES = {
    "mother":     {"emotional": ["personal", "nostalgic", "intimate"],   "weight": 1.0},
    "hospital":   {"emotional": ["clinical", "institutional"],           "setting": "lab", "weight": 1.0},
    "church":     {"emotional": ["sacred"],                              "setting": "sacred", "weight": 1.0},
    "office":     {"emotional": ["bureaucratic", "mundane"],             "setting": "office", "weight": 1.0},
    "graveyard":  {"emotional": ["melancholy", "sacred"],                "weight": 1.0},
    "prison":     {"emotional": ["threatening", "institutional"],        "weight": 1.0},
    "nursery":    {"emotional": ["intimate", "nostalgic", "personal"],   "setting": "home", "weight": 1.0},
    "playground": {"emotional": ["nostalgic", "personal"],               "weight": 1.0},
    "basement":   {"emotional": ["threatening", "liminal"],              "setting": "home", "weight": 0.9},
    "attic":      {"emotional": ["nostalgic", "liminal"],                "setting": "home", "weight": 0.9},
    "waiting":    {"emotional": ["liminal", "mundane"],                  "action": "wait", "weight": 0.9},
    "forgotten":  {"emotional": ["abandoned", "melancholy", "nostalgic"],"weight": 0.95},
    "empty":      {"emotional": ["abandoned", "melancholy", "liminal"],  "weight": 0.9},
    "silence":    {"emotional": ["liminal", "intimate", "abandoned"],    "weight": 0.85},
    "echo":       {"emotional": ["liminal", "abandoned", "nostalgic"],   "weight": 0.85},
    "threshold":  {"emotional": ["liminal", "sacred"],                   "setting": "liminal", "weight": 1.0},
    "fluorescent":{"emotional": ["clinical", "institutional", "mundane"],"style": "cold", "weight": 0.9},
    "algorithm":  {"emotional": ["institutional", "mundane"],            "weight": 0.9},
    "system":     {"emotional": ["institutional", "bureaucratic"],       "weight": 0.85},
    "dream":      {"emotional": ["intimate", "personal", "liminal"],     "weight": 0.85},
    "nightmare":  {"emotional": ["threatening", "personal"],             "weight": 0.95},
    "home":       {"emotional": ["domestic", "intimate", "comforting"],   "setting": "home", "weight": 1.0},
    "safe":       {"emotional": ["comforting", "intimate"],              "style": "safe", "weight": 1.0},
    "dangerous":  {"emotional": ["threatening"],                         "style": "threatening", "weight": 1.0},
}


# ---------------------------------------------------------------------------
# PHRASES — multi-word expressions
# ---------------------------------------------------------------------------
PHRASES = {
    "waiting room":       {"emotional": ["liminal", "bureaucratic", "mundane"],    "setting": "office", "action": "wait", "weight": 1.0},
    "living room":        {"emotional": ["domestic", "comforting", "intimate"],     "setting": "home", "weight": 1.0},
    "dining room":        {"emotional": ["domestic", "comforting"],                 "setting": "home", "weight": 1.0},
    "emergency room":     {"emotional": ["clinical", "threatening"],               "setting": "lab", "weight": 1.0},
    "operating room":     {"emotional": ["clinical", "threatening"],               "setting": "lab", "weight": 1.0},
    "used to":            {"emotional": ["nostalgic", "melancholy"],                "action": "remember", "weight": 0.8},
    "long ago":           {"emotional": ["nostalgic", "melancholy"],                "weight": 0.85},
    "years ago":          {"emotional": ["nostalgic", "melancholy"],                "weight": 0.85},
    "back when":          {"emotional": ["nostalgic", "personal"],                  "weight": 0.85},
    "no longer":          {"emotional": ["melancholy", "abandoned"],                "weight": 0.9},
    "never again":        {"emotional": ["melancholy", "threatening"],              "weight": 0.9},
    "gone forever":       {"emotional": ["melancholy", "abandoned"],                "weight": 0.95},
    "feel safe":          {"emotional": ["comforting", "intimate"],                 "style": "safe", "weight": 1.0},
    "worst memory":       {"emotional": ["threatening", "personal", "melancholy"],  "weight": 1.0},
    "worst fear":         {"emotional": ["threatening", "personal"],                "weight": 1.0},
    "happy place":        {"emotional": ["comforting", "personal", "intimate"],     "weight": 1.0},
    "childhood home":     {"emotional": ["nostalgic", "personal", "intimate"],      "setting": "home", "weight": 1.0},
    "middle of nowhere":  {"emotional": ["liminal", "abandoned"],                   "weight": 0.9},
    "dead end":           {"emotional": ["liminal", "threatening"],                 "weight": 0.85},
    "out of order":       {"emotional": ["abandoned", "mundane"],                   "weight": 0.8},
    "no way out":         {"emotional": ["threatening", "liminal"],                 "weight": 0.95},
    "burned down":        {"emotional": ["decayed", "melancholy"],                  "weight": 0.95},
    "falling apart":      {"emotional": ["decayed", "abandoned", "melancholy"],     "weight": 0.9},
    "closed down":        {"emotional": ["abandoned", "melancholy"],                "weight": 0.85},
    "locked out":         {"emotional": ["abandoned", "threatening"],               "weight": 0.85},
    "looking for":        {"emotional": ["personal", "liminal"],                    "action": "search", "weight": 0.7},
    "trying to":          {"emotional": ["personal"],                               "weight": 0.6},
    "place where":        {"emotional": ["personal", "liminal"],                    "weight": 0.65},
    "reminds me":         {"emotional": ["nostalgic", "personal"],                  "action": "remember", "weight": 0.85},
    "belongs to":         {"emotional": ["personal"],                               "weight": 0.7},
    "ran away":           {"emotional": ["threatening", "personal"],                "action": "escape", "weight": 0.9},
    "fell apart":         {"emotional": ["decayed", "melancholy"],                  "weight": 0.9},
    "left behind":        {"emotional": ["abandoned", "melancholy", "personal"],    "weight": 0.95},
    "growing up":         {"emotional": ["nostalgic", "personal"],                  "weight": 0.9},
    "getting worse":      {"emotional": ["threatening", "melancholy"],              "weight": 0.8},
    "taken away":         {"emotional": ["threatening", "melancholy", "personal"],  "weight": 0.9},
    "far away":           {"emotional": ["nostalgic", "liminal"],                   "weight": 0.75},
    "broke down":         {"emotional": ["decayed", "melancholy"],                  "weight": 0.85},
    "first time":         {"emotional": ["nostalgic", "personal"],                  "weight": 0.7},
    "last time":          {"emotional": ["melancholy", "nostalgic"],                "weight": 0.8},
    "every day":          {"emotional": ["mundane"],                                "weight": 0.6},
    "old house":          {"emotional": ["nostalgic", "domestic"],                  "setting": "home", "weight": 0.9},
    "empty room":         {"emotional": ["abandoned", "liminal"],                   "weight": 0.9},
    "dark room":          {"emotional": ["threatening", "liminal"],                 "style": "dark", "weight": 0.85},
    "bright light":       {"emotional": ["clinical", "liminal"],                    "style": "bright", "weight": 0.75},
    "cold room":          {"emotional": ["clinical", "abandoned"],                  "style": "cold", "weight": 0.85},
    "warm light":         {"emotional": ["comforting", "intimate"],                 "style": "warm", "weight": 0.85},
    "broken glass":       {"emotional": ["decayed", "threatening"],                 "weight": 0.85},
    "locked door":        {"emotional": ["liminal", "threatening"],                 "weight": 0.9},
    "open door":          {"emotional": ["liminal"],                                "weight": 0.75},
    "open window":        {"emotional": ["liminal", "intimate"],                    "weight": 0.75},
    "stained glass":      {"emotional": ["sacred", "nostalgic"],                    "weight": 0.9},
    "filing cabinet":     {"emotional": ["bureaucratic", "institutional"],          "object": "office", "weight": 0.95},
    "hospital bed":       {"emotional": ["clinical", "threatening"],                "object": "lab", "weight": 1.0},
    "school bus":         {"emotional": ["nostalgic", "institutional"],             "weight": 0.85},
    "fire escape":        {"emotional": ["liminal", "threatening"],                 "weight": 0.85},
    "parking lot":        {"emotional": ["liminal", "mundane", "public"],           "weight": 0.8},
    "bus stop":           {"emotional": ["liminal", "mundane", "public"],           "weight": 0.8},
    "train station":      {"emotional": ["liminal", "public"],                      "setting": "public", "weight": 0.9},
    "gas station":        {"emotional": ["liminal", "mundane", "public"],           "weight": 0.8},
    "rest stop":          {"emotional": ["liminal", "mundane"],                     "weight": 0.8},
    "church basement":    {"emotional": ["sacred", "liminal"],                      "setting": "sacred", "weight": 0.9},
    "front porch":        {"emotional": ["domestic", "nostalgic", "comforting"],    "setting": "home", "weight": 0.9},
    "back door":          {"emotional": ["liminal", "domestic"],                    "weight": 0.75},
    "side street":        {"emotional": ["liminal", "public"],                      "weight": 0.7},
    "strip mall":         {"emotional": ["public", "mundane"],                      "setting": "retail", "weight": 0.85},
    "corner store":       {"emotional": ["nostalgic", "public"],                    "setting": "retail", "weight": 0.85},
    "vending machine":    {"emotional": ["mundane", "institutional"],               "weight": 0.8},
    "paper trail":        {"emotional": ["bureaucratic"],                           "weight": 0.8},
    "red tape":           {"emotional": ["bureaucratic", "institutional"],          "weight": 0.9},
    "fine print":         {"emotional": ["bureaucratic"],                           "weight": 0.8},
    "bottom line":        {"emotional": ["bureaucratic", "mundane"],                "weight": 0.7},
}


# ---------------------------------------------------------------------------
# PROP NAME EXTRACTION — vocabulary from curated-props.json display names
# ---------------------------------------------------------------------------
def extract_prop_vocabulary(manifest_path: Path) -> dict:
    """Extract unique tokens from prop display names and map to emotional tags."""
    if not manifest_path.exists():
        print(f"  [WARN] Manifest not found: {manifest_path}")
        return {}

    with open(manifest_path) as f:
        manifest = json.load(f)

    token_tags = {}  # token → {emotional_tags set, groups set}

    for prop in manifest.get("props", []):
        display = prop.get("display_name", "")
        group = prop.get("group", "")
        etags = prop.get("emotional_tags", [])

        for token in re.findall(r"[a-zA-Z]+", display):
            token = token.lower()
            if len(token) < 3:
                continue
            if token not in token_tags:
                token_tags[token] = {"emotional": set(), "groups": set()}
            token_tags[token]["emotional"].update(etags)
            token_tags[token]["groups"].add(group)

    entries = {}
    for token, data in token_tags.items():
        etags = sorted(data["emotional"])[:4]
        if not etags:
            continue
        groups = data["groups"]
        obj = None
        if len(groups) == 1:
            obj = list(groups)[0]
        elif groups:
            group_priority = ["domestic", "furniture", "item", "lab", "office", "retail", "tech", "workshop"]
            for g in group_priority:
                if g in groups:
                    obj = g
                    break

        entries[token] = {"emotional": etags, "weight": 0.65}
        if obj:
            entries[token]["object"] = obj

    return entries


# ---------------------------------------------------------------------------
# BUILD — merge all sources into vocabulary.json
# ---------------------------------------------------------------------------
def build_vocabulary() -> dict:
    """Build the full vocabulary dictionary from all sources."""
    words = {}

    # 1. Process SEED categories
    for cat in SEED:
        etags = cat["emotional"]
        weight = cat.get("weight", 0.8)
        obj = cat.get("object")
        setting = cat.get("setting")
        action = cat.get("action")
        style = cat.get("style")

        for word in cat["words"].split():
            word = word.lower().strip()
            if len(word) < 2:
                continue
            if word not in words or words[word].get("weight", 0) < weight:
                entry = {"emotional": etags, "weight": weight}
                if obj: entry["object"] = obj
                if setting: entry["setting"] = setting
                if action: entry["action"] = action
                if style: entry["style"] = style
                words[word] = entry

    # 2. Apply overrides (these always win)
    for word, entry in OVERRIDES.items():
        words[word.lower()] = entry

    # 3. Extract from prop display names (lower priority — doesn't overwrite)
    prop_words = extract_prop_vocabulary(MANIFEST_PATH)
    for word, entry in prop_words.items():
        if word not in words:
            words[word] = entry

    # 4. Clean entries — omit null fields, validate tags
    cleaned = {}
    invalid_tags = set()
    for word, entry in sorted(words.items()):
        etags = [t for t in entry.get("emotional", []) if t in VALID_EMOTIONAL]
        if not etags:
            continue

        clean = {"emotional": etags}
        if entry.get("weight", 0.8) != 0.8:
            clean["weight"] = round(entry["weight"], 2)

        for field in ("object", "setting", "action", "style"):
            val = entry.get(field)
            if val:
                clean[field] = val

        invalid = set(entry.get("emotional", [])) - VALID_EMOTIONAL
        invalid_tags.update(invalid)

        cleaned[word] = clean

    if invalid_tags:
        print(f"  [WARN] Removed invalid emotional tags: {invalid_tags}")

    # 5. Build phrases
    phrases = {}
    for phrase, entry in sorted(PHRASES.items()):
        etags = [t for t in entry.get("emotional", []) if t in VALID_EMOTIONAL]
        if not etags:
            continue
        clean = {"emotional": etags}
        if entry.get("weight", 0.8) != 0.8:
            clean["weight"] = round(entry["weight"], 2)
        for field in ("object", "setting", "action", "style"):
            val = entry.get(field)
            if val:
                clean[field] = val
        phrases[phrase] = clean

    return {
        "version": 1,
        "generated": str(date.today()),
        "word_count": len(cleaned),
        "phrase_count": len(phrases),
        "words": cleaned,
        "phrases": phrases,
    }


# ---------------------------------------------------------------------------
# STATS — tag distribution report
# ---------------------------------------------------------------------------
def print_stats(vocab: dict):
    tag_counts = Counter()
    total = vocab["word_count"]

    for entry in vocab["words"].values():
        for tag in entry.get("emotional", []):
            tag_counts[tag] += 1

    print(f"\n{'='*60}")
    print(f"VOCABULARY STATS — {total} words, {vocab['phrase_count']} phrases")
    print(f"{'='*60}")
    print(f"\n{'Tag':<20} {'Count':>6} {'%':>7}")
    print(f"{'-'*35}")
    for tag in sorted(VALID_EMOTIONAL):
        count = tag_counts.get(tag, 0)
        pct = 100 * count / total if total > 0 else 0
        flag = " ⚠ LOW" if pct < 3 else (" ⚠ HIGH" if pct > 30 else "")
        print(f"{tag:<20} {count:>6} {pct:>6.1f}%{flag}")

    # Intent coverage
    obj_count = sum(1 for e in vocab["words"].values() if "object" in e)
    set_count = sum(1 for e in vocab["words"].values() if "setting" in e)
    act_count = sum(1 for e in vocab["words"].values() if "action" in e)
    sty_count = sum(1 for e in vocab["words"].values() if "style" in e)

    print(f"\nIntent coverage:")
    print(f"  object:  {obj_count:>5} ({100*obj_count/total:.1f}%)")
    print(f"  setting: {set_count:>5} ({100*set_count/total:.1f}%)")
    print(f"  action:  {act_count:>5} ({100*act_count/total:.1f}%)")
    print(f"  style:   {sty_count:>5} ({100*sty_count/total:.1f}%)")


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------
def main():
    os.chdir(Path(__file__).parent)

    if "--stats" in sys.argv:
        if OUTPUT_PATH.exists():
            with open(OUTPUT_PATH) as f:
                print_stats(json.load(f))
        else:
            print(f"No vocabulary file at {OUTPUT_PATH}")
        return

    print("Building vocabulary...")
    vocab = build_vocabulary()

    print(f"  Seed words:  {sum(len(c['words'].split()) for c in SEED)}")
    print(f"  Overrides:   {len(OVERRIDES)}")
    print(f"  Phrases:     {len(PHRASES)}")
    print(f"  Final words: {vocab['word_count']}")
    print(f"  Final phr:   {vocab['phrase_count']}")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(OUTPUT_PATH, 'w') as f:
        json.dump(vocab, f, separators=(',', ':'))

    size_kb = OUTPUT_PATH.stat().st_size / 1024
    print(f"\nWritten to: {OUTPUT_PATH} ({size_kb:.1f} KB)")

    print_stats(vocab)

    if "--expand" in sys.argv:
        try:
            import anthropic
        except ImportError:
            print("\n[ERROR] --expand requires: pip install anthropic")
            print("Set ANTHROPIC_API_KEY environment variable.")
            return
        expand_with_api(vocab)


def expand_with_api(vocab: dict):
    """Optional: expand vocabulary using Claude API."""
    import anthropic

    client = anthropic.Anthropic()
    existing_words = set(vocab["words"].keys())
    new_entries = {}

    categories_to_expand = [
        ("sacred and spiritual objects, places, and concepts", ["sacred"]),
        ("bureaucratic and administrative terms", ["bureaucratic", "institutional"]),
        ("melancholy and grief-related words", ["melancholy"]),
        ("liminal spaces and transitional concepts", ["liminal"]),
        ("childhood and nostalgia words", ["nostalgic", "personal"]),
        ("clinical and medical terminology", ["clinical"]),
        ("decay, entropy, and abandonment", ["decayed", "abandoned"]),
        ("domestic life and household items", ["domestic", "mundane"]),
        ("threatening and dangerous situations", ["threatening"]),
        ("intimate and personal experiences", ["intimate", "personal"]),
    ]

    for desc, base_tags in categories_to_expand:
        print(f"\n  Expanding: {desc}...")
        prompt = f"""Generate 200 English words related to: {desc}

These words will be used in an art installation that maps player text to emotional categories.
The 16 emotional tags are: intimate, nostalgic, personal, comforting, domestic, clinical,
institutional, bureaucratic, threatening, melancholy, abandoned, decayed, sacred, liminal, public, mundane.

For each word, provide a JSON object with:
- "word": the word (lowercase, single word only)
- "emotional": array of 1-4 emotional tags from the list above
- "weight": confidence 0.5-1.0

Output ONLY a JSON array. No explanation. Exclude these already-known words: {', '.join(sorted(list(existing_words)[:200]))}"""

        try:
            response = client.messages.create(
                model="claude-sonnet-4-6",
                max_tokens=8000,
                messages=[{"role": "user", "content": prompt}]
            )
            text = response.content[0].text
            json_match = re.search(r'\[.*\]', text, re.DOTALL)
            if json_match:
                entries = json.loads(json_match.group())
                for e in entries:
                    word = e.get("word", "").lower().strip()
                    if word and len(word) >= 2 and word not in existing_words:
                        etags = [t for t in e.get("emotional", base_tags) if t in VALID_EMOTIONAL]
                        if etags:
                            new_entries[word] = {
                                "emotional": etags,
                                "weight": round(e.get("weight", 0.75), 2)
                            }
                            existing_words.add(word)
                print(f"    +{len([e for e in entries if e.get('word','').lower() in new_entries])} new words")
        except Exception as ex:
            print(f"    [ERROR] {ex}")

    if new_entries:
        vocab["words"].update(new_entries)
        vocab["word_count"] = len(vocab["words"])
        with open(OUTPUT_PATH, 'w') as f:
            json.dump(vocab, f, separators=(',', ':'))
        print(f"\n  Expanded vocabulary: {vocab['word_count']} words total")
        print_stats(vocab)


if __name__ == "__main__":
    main()
