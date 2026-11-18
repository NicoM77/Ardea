import { Component } from '@angular/core';
import { UiCard, UiField, UiInput, UiPageHeader, UiPasswordToggle } from 'ui';

/** Fields in every state. Hover and focus are pinned with `data-ui-state` for the image. */
@Component({
  selector: 'ardea-fields-page',
  imports: [UiCard, UiField, UiInput, UiPageHeader, UiPasswordToggle],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Fields"
        sub="White, 1px Field edge, 6px corners. Errors are a second line in Text and a sentence, never red."
      />

      <div class="sc-grid">
        <ui-card heading="States">
          <div class="sc-stack">
            <ui-field label="Default"
              ><input uiInput type="text" placeholder="name@example.org"
            /></ui-field>
            <ui-field label="Hover"
              ><input uiInput type="text" value="name@example.org" data-ui-state="hover"
            /></ui-field>
            <ui-field label="Focus"
              ><input uiInput type="text" value="name@example.org" data-ui-state="focus"
            /></ui-field>
            <ui-field label="Disabled"
              ><input uiInput type="text" value="name@example.org" disabled
            /></ui-field>
            <ui-field label="Read only"
              ><input uiInput type="text" value="name@example.org" readonly
            /></ui-field>
          </div>
        </ui-card>

        <ui-card heading="Help and errors">
          <div class="sc-stack">
            <ui-field label="Display name" hint="Shown to your team next to what you change.">
              <input uiInput type="text" value="Sample Person" />
            </ui-field>
            <ui-field label="Email" error="Enter an email address, such as name@example.org.">
              <input uiInput type="email" value="name@example" />
            </ui-field>
            <ui-field
              label="Password"
              hint="At least 12 characters. A few words in a row are easy to remember."
              error="Use at least 12 characters."
            >
              <input uiInput #pw type="password" value="short" />
              <ui-password-toggle [control]="pw" />
            </ui-field>
            <ui-field label="Department" optional><input uiInput type="text" /></ui-field>
          </div>
        </ui-card>

        <ui-card heading="Kinds">
          <div class="sc-stack">
            <ui-field label="Password with toggle">
              <input uiInput #pw2 type="password" value="correct horse battery" />
              <ui-password-toggle [control]="pw2" />
            </ui-field>
            <ui-field label="Code (mono)"
              ><input uiInput mono type="text" inputmode="numeric" value="482 913"
            /></ui-field>
            <ui-field label="Select">
              <select uiInput>
                <option>All projects</option>
                <option>Example project</option>
              </select>
            </ui-field>
            <ui-field label="Note">
              <textarea uiInput rows="3">Waiting for the vendor's patch.</textarea>
            </ui-field>
          </div>
        </ui-card>
      </div>
    </div>
  `,
})
export class FieldsPage {}
