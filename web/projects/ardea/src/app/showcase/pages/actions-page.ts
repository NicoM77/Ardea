import { Component } from '@angular/core';
import {
  LucideArchive,
  LucideChevronDown,
  LucideFolderGit2,
  LucidePencil,
  LucideSlidersHorizontal,
} from '@lucide/angular';
import {
  UiBackLink,
  UiButton,
  UiCard,
  UiMenu,
  UiMenuItem,
  UiMenuTrigger,
  UiOverflowMenu,
  UiPageHeader,
} from 'ui';

@Component({
  selector: 'ardea-actions-page',
  imports: [
    LucideArchive,
    LucideChevronDown,
    LucideFolderGit2,
    LucidePencil,
    LucideSlidersHorizontal,
    UiBackLink,
    UiButton,
    UiCard,
    UiMenu,
    UiMenuItem,
    UiMenuTrigger,
    UiOverflowMenu,
    UiPageHeader,
  ],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Actions"
        sub="The frequent action stays a button. Rarer and destructive ones go into the menu at the end of the row, which names what it acts on."
      />

      <ui-card heading="Overflow menu">
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">In a header</span>
            <div class="sc-demo" style="gap: var(--space-sm)">
              <button uiButton type="button">
                <svg lucideFolderGit2 aria-hidden="true"></svg>Add repository
              </button>
              <button uiButton type="button">
                <svg lucideSlidersHorizontal aria-hidden="true"></svg>Change technologies
              </button>
              <ui-overflow-menu name="Customer Portal">
                <ng-template>
                  <div uiMenu>
                    <button uiMenuItem type="button">
                      <svg lucidePencil aria-hidden="true"></svg>Edit
                    </button>
                    <div class="ui-menu-sep" role="separator"></div>
                    <button uiMenuItem destructive type="button">
                      <svg lucideArchive aria-hidden="true"></svg>Archive
                    </button>
                  </div>
                </ng-template>
              </ui-overflow-menu>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Working</span>
            <div class="sc-demo">
              <ui-overflow-menu name="Ops webhook" busy>
                <ng-template>
                  <div uiMenu>
                    <button uiMenuItem type="button">New secret</button>
                  </div>
                </ng-template>
              </ui-overflow-menu>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Open</span>
            <div class="sc-demo">
              <div uiMenu aria-label="More actions for Customer Portal">
                <button uiMenuItem type="button">
                  <svg lucidePencil aria-hidden="true"></svg>Edit
                </button>
                <div class="ui-menu-sep" role="separator"></div>
                <button uiMenuItem destructive type="button">
                  <svg lucideArchive aria-hidden="true"></svg>Archive
                </button>
              </div>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Menu button</span>
            <div class="sc-demo">
              <button uiButton type="button" [uiMenuTrigger]="outcomes" uiMenuAlign="start">
                Set the outcome<svg lucideChevronDown aria-hidden="true"></svg>
              </button>
              <ng-template #outcomes>
                <div uiMenu>
                  <button uiMenuItem type="button">Remediated</button>
                  <button uiMenuItem type="button">Risk accepted</button>
                  <button uiMenuItem type="button">Not affected</button>
                </div>
              </ng-template>
            </div>
          </div>
        </div>
      </ui-card>

      <ui-card heading="Back link">
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">To the list</span>
            <div class="sc-demo"><ui-back-link label="Projects" link="/ui/actions" /></div>
          </div>
        </div>
      </ui-card>
    </div>
  `,
})
export class ActionsPage {}
